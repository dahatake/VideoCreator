$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot '..\scripts\grant-azure-access.ps1'
$global:targetScope = '/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/test'
$global:principal = '00000000-0000-0000-0000-000000000002'
$global:directId = "$global:targetScope/providers/Microsoft.Authorization/roleAssignments/00000000-0000-0000-0000-000000000003"
$global:childId = "$global:targetScope/providers/Microsoft.CognitiveServices/accounts/test/providers/Microsoft.Authorization/roleAssignments/00000000-0000-0000-0000-000000000004"
$global:tenantMismatch = $false
$global:assignments = @()
$global:calls = @()

function global:az {
    $global:LASTEXITCODE = 0
    $global:calls += ,@($args)
    if ($args[0] -eq 'account') {
        if ($global:tenantMismatch -and $args -contains '--subscription') { return 'other-tenant' }
        return 'target-tenant'
    }
    if ($args[0] -eq 'group') { return $global:targetScope }
    if ($args[0] -eq 'role' -and $args[2] -eq 'list') {
        if ($args -notcontains '--assignee-object-id' -or $args -notcontains '--fill-principal-name') {
            throw 'Role listing must bypass Microsoft Graph.'
        }
        return $global:assignments
    }
    if ($args[0] -eq 'role' -and $args[2] -in @('create', 'delete')) { return }
    throw "Unexpected az command: $args"
}

function Run-Script([hashtable]$scriptArgs) {
    $global:calls = @()
    & $script -User $global:principal -ResourceGroup test -SpeechOnly @scriptArgs
}

try {
    $global:assignments = @($global:childId)
    Run-Script @{}
    if (@($global:calls | Where-Object { $_[0] -eq 'role' -and $_[2] -eq 'create' }).Count -ne 1) {
        throw 'A child-scope assignment must not prevent a group-scope grant.'
    }

    $global:assignments = @($global:directId, $global:childId)
    Run-Script @{}
    if (@($global:calls | Where-Object { $_[0] -eq 'role' -and $_[2] -eq 'create' }).Count -ne 0) {
        throw 'An existing group-scope grant must not be duplicated.'
    }
    Run-Script @{ Remove = $true }
    $deletes = @($global:calls | Where-Object { $_[0] -eq 'role' -and $_[2] -eq 'delete' })
    if ($deletes.Count -ne 1 -or $deletes[0] -notcontains $global:directId -or $deletes[0] -contains $global:childId) {
        throw 'Removal must target only the exact group-scope assignment ID.'
    }

    $global:tenantMismatch = $true
    $failed = $false
    try { Run-Script @{ Subscription = 'other' } } catch { $failed = $_.Exception.Message -like '*tenant*' }
    if (-not $failed -or @($global:calls | Where-Object { $_[0] -eq 'role' }).Count -ne 0) {
        throw 'A tenant mismatch must fail before touching role assignments.'
    }
    Write-Host 'PASS: scope-safe grants/removals, idempotency, and tenant mismatch.'
} finally {
    Remove-Item function:\global:az
}
