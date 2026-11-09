namespace TriasDeparture.Alexa.ModelGenerator;

/// <summary>Sample utterances (German, no digits, slots never adjacent).</summary>
internal static class Utterances
{
    // ── Building blocks ──────────────────────────────────────────────────────

    private static readonly string[] Locations =
    [
        "in der {stop}", "an der {stop}", "bei der {stop}", "von der {stop}", "ab der {stop}",
        "am {stop}", "beim {stop}", "vom {stop}", "ab {stop}", "in {stop}", "bei {stop}", "von {stop}",
        "an der haltestelle {stop}", "bei der haltestelle {stop}", "von der haltestelle {stop}"
    ];

    private static readonly string[] CommonLocations =
        ["in der {stop}", "an der {stop}", "von der {stop}", "am {stop}", "beim {stop}", "an der haltestelle {stop}"];

    private static readonly string[] Directions =
        ["richtung {direction}", "in richtung {direction}", "nach {direction}"];

    private static readonly string[] LinePhrases =
        ["der nächste {line}", "die nächste {line}", "der {line}"];

    private static readonly string[] SecondNextLinePhrases =
        ["der übernächste {line}", "die übernächste {line}"];

    private static readonly string[] AnyLinePhrases =
    [
        "die nächste bim", "die nächste straßenbahn", "die nächste tram", "der nächste bus",
        "die übernächste bim", "der übernächste bus"
    ];

    // ── Intents ──────────────────────────────────────────────────────────────

    public static IEnumerable<string> NextDeparture()
    {
        // Main clause: "wann fährt der nächste {line} in der {stop} (richtung {direction})"
        foreach (var np in LinePhrases)
        foreach (var loc in Locations)
        {
            yield return $"wann fährt {np} {loc}";
            yield return $"wann fährt {np} {loc} richtung {{direction}}";
        }

        foreach (var loc in CommonLocations)
        foreach (var dir in Directions)
        {
            yield return $"wann fährt der nächste {{line}} {loc} {dir}";
            yield return $"wann fährt der nächste {{line}} {dir} {loc}";
            yield return $"wann kommt der nächste {{line}} {loc} {dir}";
        }

        foreach (var np in SecondNextLinePhrases)
        foreach (var loc in CommonLocations)
        {
            yield return $"wann fährt {np} {loc}";
            yield return $"wann kommt {np} {loc}";
        }

        foreach (var loc in Locations)
        {
            yield return $"wann kommt der nächste {{line}} {loc}";
            yield return $"wann kommt der {{line}} {loc}";
            yield return $"wann geht der nächste {{line}} {loc}";
        }

        // Subordinate clause after "Alexa, frag Bim Graz, …": "wann der nächste {line} in der {stop} fährt"
        foreach (var loc in Locations)
        {
            yield return $"wann der nächste {{line}} {loc} fährt";
            yield return $"wann der {{line}} {loc} fährt";
            yield return $"wann der nächste {{line}} {loc} kommt";
        }

        foreach (var loc in CommonLocations)
        {
            yield return $"wann der nächste {{line}} {loc} richtung {{direction}} fährt";
            yield return $"wann der nächste {{line}} {loc} nach {{direction}} fährt";
            yield return $"wann die nächste {{line}} {loc} fährt";
            yield return $"wann der übernächste {{line}} {loc} fährt";
        }

        // Any line: "wann fährt die nächste bim in der {stop}"
        foreach (var np in AnyLinePhrases)
        foreach (var loc in CommonLocations)
        {
            yield return $"wann fährt {np} {loc}";
            yield return $"wann {np} {loc} fährt";
        }

        foreach (var loc in CommonLocations)
        {
            yield return $"wann fährt die nächste bim {loc} richtung {{direction}}";
            yield return $"wann fährt der nächste bus {loc} richtung {{direction}}";
            yield return $"was fährt als nächstes {loc}";
            yield return $"welche abfahrten gibt es {loc}";
        }

        // Without stop → home stop
        foreach (var np in LinePhrases.Concat(SecondNextLinePhrases))
        {
            yield return $"wann fährt {np}";
            yield return $"wann kommt {np}";
            yield return $"wann fährt {np} richtung {{direction}}";
        }

        yield return "wann der nächste {line} fährt";
        yield return "wann der nächste {line} kommt";
        yield return "wann der nächste {line} richtung {direction} fährt";
        yield return "wann die nächste bim fährt";
        yield return "wann der nächste bus fährt";
        yield return "wann fährt die nächste bim";
        yield return "wann fährt der nächste bus";
        yield return "wann fährt die nächste bim richtung {direction}";

        // Follow-up answer to "Für welche Haltestelle?"
        yield return "{stop}";
        yield return "haltestelle {stop}";
        yield return "die haltestelle {stop}";
        foreach (var loc in Locations.Where(l => !l.Contains("haltestelle")))
            yield return loc;
    }

    public static IEnumerable<string> SetHomeStop() =>
    [
        "meine haltestelle ist {stop}",
        "meine haltestelle ist die {stop}",
        "meine haltestelle ist der {stop}",
        "meine heimathaltestelle ist {stop}",
        "speichere {stop} als meine haltestelle",
        "speichere {stop} als heimathaltestelle",
        "speichere die haltestelle {stop}",
        "setze meine haltestelle auf {stop}",
        "ändere meine haltestelle auf {stop}",
        "ändere meine haltestelle zu {stop}",
        "ich wohne bei der {stop}",
        "ich wohne bei {stop}",
        "ich wohne in der nähe von {stop}",
        "meine haltestelle speichern",
        "haltestelle speichern",
        "meine haltestelle ändern"
    ];

    public static IEnumerable<string> GetHomeStop() =>
    [
        "welche haltestelle hast du gespeichert",
        "welche haltestelle ist gespeichert",
        "was ist meine haltestelle",
        "wie heißt meine haltestelle",
        "welche ist meine haltestelle",
        "welche haltestelle ist meine haltestelle",
        "was hast du als meine haltestelle gespeichert"
    ];
}
