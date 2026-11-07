// Generates the Alexa interaction model (alexa/skill-package/interactionModels/custom/de-DE.json)
// from live TRIAS data: all stops in Graz, the lines and destinations seen at the main hubs.
//
//   dotnet run --project alexa/tools/TriasDeparture.Alexa.ModelGenerator
//
// TRIAS endpoint: env var TRIAS_BASE_URL, otherwise api/TriasDeparture.Api/appsettings.Development.json.

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TriasDeparture.Alexa.ModelGenerator;
using TriasDeparture.Alexa.Speech;
using TriasDeparture.Infrastructure.Trias;

const string InvocationName = "glei";

// Bounding box around Graz – the stop list is filtered to locality "Graz" afterwards
const double West = 15.33, North = 47.13, East = 15.52, South = 46.98;

// Hubs whose departures cover (almost) every line and destination in Graz
string[] hubs =
[
    "Jakominiplatz", "Hauptplatz Congress", "Hauptbahnhof", "Griesplatz", "Lendplatz", "Südtiroler Platz Kunsthaus",
    "Andritz", "Puntigam", "Liebenau Murpark", "Mariatrost", "Reininghaus", "Eggenberg UKH", "Geidorfplatz",
    "Schillerplatz", "Webling", "Gösting", "Krenngasse", "Laudongasse", "Smart City", "Wetzelsdorf"
];

// Districts people use as direction ("Richtung Andritz") even when no stop is called exactly that
string[] districts =
[
    "Innere Stadt", "Sankt Leonhard", "Geidorf", "Lend", "Gries", "Jakomini", "Liebenau", "Sankt Peter",
    "Waltendorf", "Ries", "Mariatrost", "Andritz", "Gösting", "Eggenberg", "Wetzelsdorf", "Straßgang", "Puntigam",
    "Innenstadt", "Zentrum", "Stadtmitte", "Uni", "Universität", "Bahnhof", "Flughafen", "Seiersberg", "Raaba"
];

var root     = FindRepoRoot();
var baseUrl  = Environment.GetEnvironmentVariable("TRIAS_BASE_URL") ?? ReadBaseUrl(root);
var output   = Path.Combine(root, "alexa", "skill-package", "interactionModels", "custom", "de-DE.json");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var builder  = new TriasXmlBuilder("glei-model-generator");
var parser   = new TriasXmlParser();

// ── 1. Stops ─────────────────────────────────────────────────────────────────

Console.WriteLine("Loading stops in Graz …");
var grazStops = await LoadStopsAsync();
Console.WriteLine($"  {grazStops.Count} stops");

// Several TRIAS stops may clean up to the same name (e.g. "X" and "X/tim") – prefer the plain one
var stopValues = grazStops
    .GroupBy(s => TextNormalizer.Normalize(StopNames.Value(s.Name)))
    .Select(g => g.OrderBy(s => s.Name.Contains("/tim", StringComparison.OrdinalIgnoreCase)).ThenBy(s => s.Name.Length).First())
    .Select(s => (s.Id, s.Name, Value: StopNames.Value(s.Name)))
    .OrderBy(s => s.Value, StringComparer.Create(CultureInfo.GetCultureInfo("de-AT"), true))
    .ToList();

// ── 2. Lines and destinations at the hubs ────────────────────────────────────

Console.WriteLine("Sampling departures at hubs …");
var lines        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var hub in hubs)
{
    var stop = stopValues.FirstOrDefault(s => TextNormalizer.Normalize(s.Value) == TextNormalizer.Normalize(hub))
               is { Id: not null } exact ? exact
               : stopValues.FirstOrDefault(s => TextNormalizer.Normalize(s.Value).StartsWith(TextNormalizer.Normalize(hub)));
    if (stop.Id is null)
    {
        Console.WriteLine($"  ! hub '{hub}' not found");
        continue;
    }

    var departures = parser.ParseDepartures(await PostAsync(builder.BuildStopEventRequest(stop.Id, DateTime.UtcNow, 100)));
    foreach (var d in departures)
    {
        lines.Add(d.Line.Trim());
        destinations.Add(d.Destination.Trim());
    }
    Console.WriteLine($"  {stop.Value}: {departures.Count} departures");
}
Console.WriteLine($"  lines: {string.Join(", ", lines.OrderBy(l => l.Length).ThenBy(l => l))}");

// ── 3. Slot types ────────────────────────────────────────────────────────────

var stopType      = new SlotType("STOP", BuildStopValues());
var lineType      = new SlotType("LINE", BuildLineValues());
var directionType = new SlotType("DIRECTION", BuildDirectionValues());

// ── 4. Intents ───────────────────────────────────────────────────────────────

IntentDefinition[] intents =
[
    new("NextDepartureIntent",
        [new("line", "LINE"), new("stop", "STOP"), new("direction", "DIRECTION")],
        Utterances.NextDeparture().Distinct().ToList()),
    new("SetHomeStopIntent", [new("stop", "STOP")], Utterances.SetHomeStop().ToList()),
    new("GetHomeStopIntent", [], Utterances.GetHomeStop().ToList()),
    new("AMAZON.CancelIntent", [], []),
    new("AMAZON.HelpIntent", [], []),
    new("AMAZON.StopIntent", [], []),
    new("AMAZON.NavigateHomeIntent", [], []),
    new("AMAZON.FallbackIntent", [], [])
];

var model = new InteractionModelFile(new InteractionModel(
    new LanguageModel(InvocationName, intents, [stopType, lineType, directionType])));

// ── 5. Validate + write ──────────────────────────────────────────────────────

var errors = ModelValidator.Validate(model).ToList();
if (errors.Count > 0)
{
    Console.Error.WriteLine($"Model is invalid ({errors.Count} problems):");
    foreach (var e in errors.Take(50)) Console.Error.WriteLine($"  - {e}");
    return 1;
}

var json = JsonSerializer.Serialize(model, new JsonSerializerOptions
{
    PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    WriteIndented          = true
});
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, json + "\n", new UTF8Encoding(false));

Console.WriteLine($"""
    Wrote {Path.GetRelativePath(root, output)}
      STOP: {stopType.Values.Count} values, LINE: {lineType.Values.Count}, DIRECTION: {directionType.Values.Count}
      NextDepartureIntent: {intents[0].Samples.Count} utterances
    """);
return 0;

// ── Slot values ──────────────────────────────────────────────────────────────

List<SlotTypeValue> BuildStopValues()
{
    var names = stopValues.Select(s => s.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
    return stopValues.Select(s =>
    {
        // A synonym must not be another stop's main name, or "Griesplatz" could never mean the stop "Griesplatz"
        var synonyms = StopNames.Synonyms(s.Name)
            .Where(syn => !names.Contains(syn))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new SlotTypeValue(s.Id, new SlotValueName(s.Value, synonyms.Count > 0 ? synonyms : null));
    }).ToList();
}

List<SlotTypeValue> BuildLineValues()
{
    var values = new List<SlotTypeValue>();

    // Numeric lines 1–99 cover every tram and bus, also ones not seen at the hubs today
    for (var n = 1; n <= GermanNumbers.Max; n++)
    {
        var cardinal   = GermanNumbers.Cardinal(n);
        var colloquial = GermanNumbers.Colloquial(n);
        string[] synonyms =
        [
            $"{n}er", $"linie {cardinal}", $"linie {n}", $"{colloquial} bim", $"{colloquial} bus",
            $"bim {cardinal}", $"bus {cardinal}", $"straßenbahn {cardinal}", $"nummer {cardinal}",
            .. n == 7 ? ["siebner"] : Array.Empty<string>()
        ];
        values.Add(new SlotTypeValue(n.ToString(CultureInfo.InvariantCulture), new SlotValueName(colloquial, synonyms)));
    }

    // Lines with letters: night buses N1–N9 plus whatever the hubs show ("64E", "S5")
    var lettered = lines.Where(l => !LineNames.IsNumeric(l))
        .Concat(Enumerable.Range(1, 9).Select(n => $"N{n}"))
        .Distinct(StringComparer.OrdinalIgnoreCase);
    foreach (var line in lettered)
    {
        var match = Regex.Match(line, @"^(?<prefix>[A-Za-z]?)(?<number>\d{1,2})(?<suffix>[A-Za-z]?)$");
        if (!match.Success || int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture) is < 1 or > 99)
            continue; // trains like "REX 3" are not served by this skill

        var prefix   = match.Groups["prefix"].Value.ToLowerInvariant();
        var suffix   = match.Groups["suffix"].Value.ToLowerInvariant();
        var number   = int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
        var cardinal = GermanNumbers.Cardinal(number);
        var spoken   = string.Join(' ', new[] { prefix, cardinal, suffix }.Where(p => p.Length > 0));

        var synonyms = new List<string> { line.ToUpperInvariant(), $"linie {spoken}" };
        if (prefix == "n") synonyms.AddRange([$"nachtbus {cardinal}", $"nachtbus n {cardinal}", $"en {cardinal}"]);
        if (prefix == "s") synonyms.Add($"s-bahn {cardinal}");
        if (suffix.Length > 0 && prefix.Length == 0) synonyms.Add($"{GermanNumbers.Colloquial(number)} {suffix}");

        values.Add(new SlotTypeValue(line.ToUpperInvariant(), new SlotValueName(spoken, synonyms)));
    }

    return values;
}

List<SlotTypeValue> BuildDirectionValues() =>
    stopValues.Select(s => s.Value)
        .Concat(destinations.Select(StopNames.Value))
        .Concat(districts)
        .Where(v => v.Length > 1)
        .DistinctBy(TextNormalizer.Normalize)
        .Order(StringComparer.Create(CultureInfo.GetCultureInfo("de-AT"), true))
        .Select(v => new SlotTypeValue(null, new SlotValueName(v, null)))
        .ToList();

// ── TRIAS ────────────────────────────────────────────────────────────────────

async Task<List<(string Id, string Name)>> LoadStopsAsync()
{
    var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    var request = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Trias version="1.2" xmlns="http://www.vdv.de/trias" xmlns:siri="http://www.siri.org.uk/siri">
          <ServiceRequest>
            <siri:RequestTimestamp>{timestamp}</siri:RequestTimestamp>
            <siri:RequestorRef>glei-model-generator</siri:RequestorRef>
            <RequestPayload>
              <LocationInformationRequest>
                <InitialInput>
                  <GeoRestriction>
                    <Rectangle>
                      <UpperLeft><Longitude>{West.ToString(CultureInfo.InvariantCulture)}</Longitude><Latitude>{North.ToString(CultureInfo.InvariantCulture)}</Latitude></UpperLeft>
                      <LowerRight><Longitude>{East.ToString(CultureInfo.InvariantCulture)}</Longitude><Latitude>{South.ToString(CultureInfo.InvariantCulture)}</Latitude></LowerRight>
                    </Rectangle>
                  </GeoRestriction>
                </InitialInput>
                <Restrictions>
                  <Type>stop</Type>
                  <NumberOfResults>3000</NumberOfResults>
                </Restrictions>
              </LocationInformationRequest>
            </RequestPayload>
          </ServiceRequest>
        </Trias>
        """;

    XNamespace ns = "http://www.vdv.de/trias";
    var doc = XDocument.Parse(await PostAsync(request));
    return doc.Descendants(ns + "Location")
        .Where(l => l.Element(ns + "LocationName")?.Element(ns + "Text")?.Value == "Graz")
        .Select(l => l.Element(ns + "StopPoint"))
        .Where(sp => sp is not null)
        .Select(sp => (Id: sp!.Element(ns + "StopPointRef")?.Value ?? "",
                       Name: sp.Element(ns + "StopPointName")?.Element(ns + "Text")?.Value ?? ""))
        .Where(s => s.Id.Length > 0 && s.Name.Length > 0)
        .ToList();
}

async Task<string> PostAsync(string xml)
{
    var content = new ByteArrayContent(Encoding.UTF8.GetBytes(xml));
    content.Headers.ContentType = new("text/xml");
    using var response = await http.PostAsync(baseUrl, content);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsStringAsync();
}

// ── Setup helpers ────────────────────────────────────────────────────────────

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "TriasDeparture.slnx")))
            return dir.FullName;
    throw new InvalidOperationException("Repository root (TriasDeparture.slnx) not found.");
}

static string ReadBaseUrl(string root)
{
    var path = Path.Combine(root, "api", "TriasDeparture.Api", "appsettings.Development.json");
    var url  = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?["Trias"]?["BaseUrl"]?.GetValue<string>() : null;
    return url ?? throw new InvalidOperationException($"Set TRIAS_BASE_URL or Trias:BaseUrl in {path}.");
}
