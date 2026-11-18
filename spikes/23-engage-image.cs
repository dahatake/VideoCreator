// Spike 23: Viva Engage image end to end against the real Microsoft Foundry resource, with independent checks.
//  1. Takes the Viva Engage post that step 6 wrote (<video>.post.json) and runs the app's own code path
//     (EngageImage.WriteAsync: specified prompt → English rewrite by the chat deployment → image deployment → FFmpeg 1000x200).
//  2. Measures the PNG with ffprobe (must be exactly 1000x200).
//  3. Asks the chat deployment (vision) — not the model that drew it — whether the picture is anime style, shows an MC and
//     employees talking excitedly, and is free of text/logos; prints its verdict next to the prompts.
// Usage: dotnet run spikes/23-engage-image.cs <post.json> <out-dir> [image-deployment=FLUX-1.1-pro]
// Uses appsettings.json (VideoCreator:Llm:Endpoint / Deployment) and DefaultAzureCredential (az login).
#:property PublishAot=false
#:property NoWarn=$(NoWarn);OPENAI001
#:project ../src/VideoCreator.Core/VideoCreator.Core.csproj
using System.ClientModel;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using VideoCreator.Media;
using VideoCreator.Rendering;
using VideoCreator.Selection;

var root = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", ".."));
if (args.Length < 2) { Console.Error.WriteLine("usage: dotnet run spikes/23-engage-image.cs <post.json> <out-dir> [image-deployment]"); return 2; }
var imageDeployment = args.Length > 2 ? args[2] : "FLUX-1.1-pro";
using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "VideoCreator.App", "appsettings.json")));
var llm = settings.RootElement.GetProperty("VideoCreator").GetProperty("Llm");
var connection = new FoundryConnection(llm.GetProperty("Endpoint").GetString()!, llm.GetProperty("Deployment").GetString()!, null);
var endpoint = FoundryEndpoint.Normalize(connection.Endpoint);
var auth = FoundryAuth.Policy(connection, FoundryAuth.DefaultCredential);

var post = JsonDocument.Parse(File.ReadAllText(args[0])).RootElement.GetProperty("engage").GetString()!;
Directory.CreateDirectory(args[1]);
var path = Path.Combine(args[1], "engage.png");
var ff = new FFmpeg(Path.Combine(root, "tools", "ffmpeg", "bin"), Path.Combine(root, "tools", "fonts"));
var started = DateTime.UtcNow;
var provenance = await EngageImage.WriteAsync(ff, new FoundryImageGenerator(endpoint, imageDeployment, auth), post, imageDeployment, path, default,
    new AzureOpenAISelector(endpoint, connection.Deployment, auth), Console.WriteLine);
File.WriteAllText(Path.Combine(args[1], "engage-image.json"), VideoCreator.Json.Serialize(provenance, true));
Console.WriteLine($"generated in {(DateTime.UtcNow - started).TotalSeconds:F1}s → {path}");
Console.WriteLine("model prompt: " + provenance.ModelPrompt);

var probe = await ff.RunProbeAsync(["-v", "error", "-show_entries", "stream=width,height,codec_name", "-of", "json", path], default);
using var info = JsonDocument.Parse(probe.StdOut);
var stream = info.RootElement.GetProperty("streams")[0];
var size = (stream.GetProperty("width").GetInt32(), stream.GetProperty("height").GetInt32());
Console.WriteLine($"ffprobe: {stream.GetProperty("codec_name").GetString()} {size.Item1}x{size.Item2}");

var judge = new ChatClient(connection.Deployment, auth, new OpenAIClientOptions { Endpoint = endpoint });
ChatCompletion verdict = await judge.CompleteChatAsync([
    new SystemChatMessage("You audit a generated banner image. Answer only with JSON: {\"anime_style\":bool,\"people\":int,\"mc_and_employees_talking_excitedly\":bool,\"visible_text_or_logo\":bool,\"note\":string}."),
    new UserChatMessage(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(File.ReadAllBytes(path)), "image/png")),
], new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat() });
var verdictText = verdict.Content[0].Text;
File.WriteAllText(Path.Combine(args[1], "verdict.json"), verdictText);
Console.WriteLine("independent check: " + verdictText);
using var v = JsonDocument.Parse(verdictText);
var pass = size == (1000, 200) && v.RootElement.GetProperty("anime_style").GetBoolean()
    && v.RootElement.GetProperty("mc_and_employees_talking_excitedly").GetBoolean() && !v.RootElement.GetProperty("visible_text_or_logo").GetBoolean();
Console.WriteLine(pass ? "PASS" : "FAIL");
return pass ? 0 : 1;
