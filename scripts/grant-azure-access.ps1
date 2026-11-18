<#
.SYNOPSIS
  Let another user call VideoCreator's Azure AI resource with Microsoft Entra ID.

.DESCRIPTION
  Assigns (or with -Remove, deletes) the data-plane roles VideoCreator needs at
  resource-group scope:
    Cognitive Services Speech User   (captions / Fast Transcription)
    Cognitive Services OpenAI User   (AI selection, telops, embeddings)
  Owner / Contributor on the group do NOT grant these data actions.
  The caller needs Owner, User Access Administrator or RBAC Administrator.
  Idempotent: existing assignments are reported, not duplicated.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\grant-azure-access.ps1 -User alice@contoso.com
  powershell -ExecutionPolicy Bypass -File scripts\grant-azure-access.ps1 -User <object-id> -Remove
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$User,   # UPN, mail (guest) or object ID in the resource tenant
    [string]$ResourceGroup = 'rg-videocreator-jpe',
    [string]$Subscription,
    [switch]$SpeechOnly,                            # captions only, no LLM
    [switch]$Remove,
    [ValidateSet('User', 'Group', 'ServicePrincipal')][string]$PrincipalType = 'User'
)
$ErrorActionPreference = 'Stop'

# Returns trimmed tsv lines. Windows PowerShell 5.1 turns native stderr into terminating errors
# under 'Stop', and ConvertFrom-Json '[]' yields one item, so use tsv + JMESPath instead of JSON.
function Invoke-Az([string[]]$azArgs, [switch]$AllowFailure) {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $out = & az @azArgs -o tsv --only-show-errors 2>&1 } finally { $ErrorActionPreference = $prev }
    $text = @($out | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) {
        if ($AllowFailure) { return $null }
        throw "az $($azArgs[0..2] -join ' ') failed: $($text -join ' ')"
    }
    return ,@($text | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() })
}

$subArgs = @()
if ($Subscription) { $subArgs = @('--subscription', $Subscription) }
$currentTenant = (Invoke-Az @('account', 'show', '--query', 'tenantId'))[0]
$tenant = (Invoke-Az (@('account', 'show', '--query', 'tenantId') + $subArgs))[0]
if (-not $tenant -or $currentTenant -ne $tenant) {
    throw "The selected subscription belongs to tenant '$tenant', but Azure CLI uses tenant '$currentTenant' for directory lookup. Sign in to the target tenant and set its subscription as the default."
}
$scope = (Invoke-Az (@('group', 'show', '-n', $ResourceGroup, '--query', 'id') + $subArgs))[0]

if ($User -match '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$') {
    $objectId = $User
} elseif ($PrincipalType -eq 'Group') {
    $objectId = (Invoke-Az @('ad', 'group', 'show', '--group', $User, '--query', 'id'))[0]
} elseif ($PrincipalType -eq 'ServicePrincipal') {
    throw 'For a service principal, specify its object ID.'
} else {
    $objectId = $null
    $found = Invoke-Az @('ad', 'user', 'show', '--id', $User, '--query', 'id') -AllowFailure
    if ($found) { $objectId = $found[0] }
    if (-not $objectId) {
        # Guests have an #EXT# UPN; look them up by mail instead.
        $safe = $User.Replace("'", "''")
        $ids = Invoke-Az @('ad', 'user', 'list', '--filter', "mail eq '$safe'", '--query', '[].id') -AllowFailure
        if ($null -eq $ids) { throw "Could not look up '$User' in Microsoft Graph (no permission or Conditional Access). Pass the user's object ID from Entra ID > Users instead." }
        if ($ids.Count -eq 1) { $objectId = $ids[0] }
        elseif ($ids.Count -gt 1) { throw "More than one user matches '$User'. Pass the object ID." }
    }
    if (-not $objectId) { throw "No user matched '$User'. Confirm the user is in the target tenant and pass their object ID from Entra ID > Users." }
}
if (-not $objectId) { throw "Could not resolve '$User' to an object ID in the target tenant." }

$roles = [ordered]@{ 'Cognitive Services Speech User' = 'f2dc8367-1007-4938-bd23-fe263f013447' }
if (-not $SpeechOnly) { $roles['Cognitive Services OpenAI User'] = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd' }

Write-Host "Scope    : $scope"
Write-Host "Principal: $User ($objectId)"
foreach ($name in $roles.Keys) {
    $roleId = $roles[$name]
    $assignmentPrefix = "$scope/providers/Microsoft.Authorization/roleAssignments/"
    $assignmentIds = Invoke-Az (@('role', 'assignment', 'list', '--assignee-object-id', $objectId, '--fill-principal-name', 'false', '--role', $roleId, '--scope', $scope, '--query', '[].id') + $subArgs)
    $existing = @($assignmentIds | Where-Object {
        $_.StartsWith($assignmentPrefix, [StringComparison]::OrdinalIgnoreCase) -and $_.Substring($assignmentPrefix.Length) -match '^[0-9a-fA-F-]{36}$'
    })
    if ($Remove) {
        if ($existing.Count -eq 0) { Write-Host "  [skip]    $name (not assigned here)"; continue }
        foreach ($assignmentId in $existing) {
            Invoke-Az (@('role', 'assignment', 'delete', '--ids', $assignmentId) + $subArgs) | Out-Null
        }
        Write-Host "  [removed] $name"
    } elseif ($existing.Count -gt 0) {
        Write-Host "  [exists]  $name"
    } else {
        Invoke-Az (@('role', 'assignment', 'create', '--assignee-object-id', $objectId, '--assignee-principal-type', $PrincipalType, '--role', $roleId, '--scope', $scope, '--query', 'id') + $subArgs) | Out-Null
        Write-Host "  [added]   $name"
    }
}

if ($Remove) {
    Write-Host 'Also check group memberships and assignments inherited from the subscription or set on the resource itself.'
} else {
    Write-Host ''
    Write-Host 'Role assignments can take a few minutes to apply. Then, on the user''s PC:'
    Write-Host "  az login --tenant $tenant"
    Write-Host '  VideoCreator > Settings > AI connection > Microsoft Entra ID > Test connection'
}
