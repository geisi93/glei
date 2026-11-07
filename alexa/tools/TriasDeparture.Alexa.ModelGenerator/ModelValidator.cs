using System.Text.RegularExpressions;

namespace TriasDeparture.Alexa.ModelGenerator;

/// <summary>Checks the rules the Alexa console enforces when building the model, so errors show up here first.</summary>
internal static partial class ModelValidator
{
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex SlotReference();

    [GeneratedRegex(@"\}\s*\{")]
    private static partial Regex AdjacentSlots();

    // Lower-case letters (incl. umlauts), spaces, apostrophes, periods, hyphens and {slot} references
    [GeneratedRegex(@"^[\p{Ll} .'\-{}a-zA-Z]+$")]
    private static partial Regex AllowedUtterance();

    private static readonly string[] ForbiddenInvocationWords = ["alexa", "amazon", "echo", "skill", "app", "launch", "ask", "tell", "open"];

    public static IEnumerable<string> Validate(InteractionModelFile file)
    {
        var model = file.InteractionModel.LanguageModel;

        if (model.InvocationName != model.InvocationName.ToLowerInvariant())
            yield return "Invocation name must be lower-case.";
        foreach (var word in model.InvocationName.Split(' ').Where(ForbiddenInvocationWords.Contains))
            yield return $"Invocation name must not contain '{word}'.";

        // ── Utterances ───────────────────────────────────────────────────────
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var intent in model.Intents)
        {
            var slotNames = intent.Slots.Select(s => s.Name).ToHashSet();
            foreach (var sample in intent.Samples)
            {
                if (sample.Any(char.IsDigit))
                    yield return $"{intent.Name}: digits in \"{sample}\" (spell numbers out).";
                if (!AllowedUtterance().IsMatch(sample))
                    yield return $"{intent.Name}: unsupported characters in \"{sample}\".";
                if (AdjacentSlots().IsMatch(sample))
                    yield return $"{intent.Name}: adjacent slots in \"{sample}\".";
                foreach (Match m in SlotReference().Matches(sample))
                    if (!slotNames.Contains(m.Groups[1].Value))
                        yield return $"{intent.Name}: unknown slot {{{m.Groups[1].Value}}} in \"{sample}\".";
                if (!seen.TryAdd(sample, intent.Name))
                    yield return $"Duplicate utterance \"{sample}\" in {intent.Name} and {seen[sample]}.";
            }
        }

        // ── Slot types ───────────────────────────────────────────────────────
        var usedTypes = model.Intents.SelectMany(i => i.Slots).Select(s => s.Type).Where(t => !t.StartsWith("AMAZON."));
        foreach (var type in usedTypes.Distinct().Where(t => model.Types.All(x => x.Name != t)))
            yield return $"Slot type {type} is used but not defined.";

        foreach (var type in model.Types)
        {
            foreach (var dup in type.Values.GroupBy(v => v.Name.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                yield return $"{type.Name}: duplicate value \"{dup.Key}\".";
            foreach (var dup in type.Values.Where(v => v.Id is not null).GroupBy(v => v.Id).Where(g => g.Count() > 1))
                yield return $"{type.Name}: duplicate id \"{dup.Key}\".";

            foreach (var value in type.Values)
            {
                var all = new[] { value.Name.Value }.Concat(value.Name.Synonyms ?? []).ToList();
                foreach (var text in all.Where(t => t.IndexOfAny(['/', '(', ')', '{', '}']) >= 0))
                    yield return $"{type.Name}: unsupported character in \"{text}\".";
                foreach (var text in all.Where(string.IsNullOrWhiteSpace))
                    yield return $"{type.Name}: empty value or synonym for id {value.Id}.";
                foreach (var dup in all.GroupBy(t => t, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                    yield return $"{type.Name}: \"{dup.Key}\" listed twice for \"{value.Name.Value}\".";
            }
        }
    }
}
