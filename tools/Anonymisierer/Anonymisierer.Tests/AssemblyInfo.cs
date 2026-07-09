using Xunit;

// Anonymizer haelt seinen Zustand (Mapping-Dictionary, Zaehler) in statischen Feldern.
// Das ist fuer die Desktop-App gedacht (ein Ordner = ein Prozess), aber nicht thread-safe.
// xUnit fuehrt Testklassen standardmaessig parallel in unterschiedlichen Collections aus;
// ohne diese Einstellung ueberschreiben sich ContextRetentionTests und
// CrossDocumentConsistencyTests gegenseitig das Anonymizer-Mapping und erzeugen
// nicht-deterministische Fehlschlaege.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
