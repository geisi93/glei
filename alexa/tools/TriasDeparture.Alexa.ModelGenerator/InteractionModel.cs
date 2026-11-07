namespace TriasDeparture.Alexa.ModelGenerator;

// Alexa interaction model JSON (as imported in the developer console's JSON editor).

internal sealed record InteractionModelFile(InteractionModel InteractionModel);

internal sealed record InteractionModel(LanguageModel LanguageModel);

internal sealed record LanguageModel(string InvocationName, IReadOnlyList<IntentDefinition> Intents, IReadOnlyList<SlotType> Types);

internal sealed record IntentDefinition(string Name, IReadOnlyList<SlotDefinition> Slots, IReadOnlyList<string> Samples);

internal sealed record SlotDefinition(string Name, string Type);

internal sealed record SlotType(string Name, IReadOnlyList<SlotTypeValue> Values);

internal sealed record SlotTypeValue(string? Id, SlotValueName Name);

internal sealed record SlotValueName(string Value, IReadOnlyList<string>? Synonyms);
