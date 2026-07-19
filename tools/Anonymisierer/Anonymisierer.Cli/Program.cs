using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Anonymisierer;

namespace Anonymisierer.Cli
{
    // Kommandozeilen-Runner fuer den Akzeptanztest (Task #200):
    // liest alle unterstuetzten Dokumente aus einem Ordner, anonymisiert sie mit der
    // echten Produktions-Engine (Anonymisierer.Core) und schreibt fuer jedes Dokument
    // eine "<name>_anon.txt" sowie eine gemeinsame "entities.json" mit allen erkannten
    // Entitaeten + Aliasen (Basis fuer den von Python erzeugten anon_report.md).
    public static class Program
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".rtf", ".odt", ".docx", ".eml", ".txt"
        };

        public static async Task<int> Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: Anonymisierer.Cli <docsFolder> <outFolder>");
                return 1;
            }

            var docsFolder = args[0];
            var outFolder = args[1];

            if (!Directory.Exists(docsFolder))
            {
                Console.Error.WriteLine($"Dokumenten-Ordner nicht gefunden: {docsFolder}");
                return 1;
            }
            Directory.CreateDirectory(outFolder);

            var files = Directory.GetFiles(docsFolder)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                Console.Error.WriteLine($"Keine unterstuetzten Testdokumente in {docsFolder} gefunden.");
                return 1;
            }

            Anonymizer.SetMappingFile(outFolder);
            Anonymizer.SetLogCallback(msg => Console.WriteLine($"[Anonymizer] {msg}"));

            // Phase 1: alle Dokumente einzeln anonymisieren (Ergebnis erst im Speicher halten,
            // noch nicht auf Platte schreiben).
            var pending = new List<(string DocName, string BaseName, string Original, string Anonymized, List<Entity> Entities)>();

            foreach (var file in files)
            {
                var docName = Path.GetFileName(file);
                // Volle Dateiendung im Ausgabenamen behalten (z.B. "Dilara Frau Ruck.odt" vs.
                // "Dilara Frau Ruck.rtf"), da sonst gleichnamige Dokumente mit unterschiedlicher
                // Endung sich gegenseitig ueberschreiben wuerden.
                var baseName = docName;
                string original;
                try
                {
                    original = Anonymizer.ReadDocument(file);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Fehler beim Lesen von '{docName}': {ex.Message}");
                    continue;
                }

                var entities = new List<Entity>();
                var anonymized = await Anonymizer.AnonymizeTextAsync(original, entities);

                if (Environment.GetEnvironmentVariable("ANON_CLI_DUMP_ORIGINAL") == "1")
                    File.WriteAllText(Path.Combine(outFolder, baseName + "_orig.txt"), original);

                pending.Add((docName, baseName, original, anonymized, entities));
            }

            // Phase 2: Cross-Dokument-Sweep. Manche Entitaeten (z.B. eine Stadt) werden erst in
            // einem spaeter verarbeiteten Dokument ueber ein Formular-Label eindeutig als PII
            // erkannt (z.B. "Wohnort\nStuttgart"), tauchen aber "nackt" (ohne Label-/PLZ-Kontext,
            // z.B. in einer Fusszeile oder Unterschriftszeile) bereits in einem frueher
            // verarbeiteten Dokument auf. Erst wenn alle Dokumente durchlaufen sind, ist das
            // Gesamt-Mapping vollstaendig — daher hier ein abschliessender Sweep ueber alle
            // bereits anonymisierten Texte, bevor sie geschrieben werden. Aus demselben Grund
            // wird auch der Ausgabe-Dateiname erst hier (statt in Phase 1) ueber
            // AnonymizeFileName() bestimmt: nur so kann z.B. "KFZ Anmeldung Ali Erkol.pdf" noch
            // auf den bereits aus einem anderen Dokument bekannten Alias von "Ali Izzet Erkol"
            // treffen, selbst wenn das KFZ-Dokument selbst keinen extrahierbaren Personennamen
            // im Text liefert (z.B. gescanntes PDF ohne OCR).
            var docReports = new List<DocReport>();
            foreach (var item in pending)
            {
                var finalText = Anonymizer.ApplyKnownMappings(item.Anonymized, item.Entities);
                var aliasedFileName = Anonymizer.AnonymizeFileName(item.DocName);
                var anonPath = Path.Combine(outFolder, aliasedFileName + "_anon.txt");
                File.WriteAllText(anonPath, finalText);

                docReports.Add(new DocReport
                {
                    Document = item.DocName,
                    AnonTextFile = Path.GetFileName(anonPath),
                    OriginalLength = item.Original.Length,
                    AnonymizedLength = finalText.Length,
                    Entities = item.Entities.Select(e => new EntityDto
                    {
                        Text = e.Text,
                        Type = e.Type.ToString(),
                        Alias = e.AnonymizedText
                    }).ToList()
                });

                Console.WriteLine($"OK: {item.DocName} -> {Path.GetFileName(anonPath)} ({item.Entities.Count} Entitaeten)");
            }

            Anonymizer.SaveMapping();

            var entitiesJsonPath = Path.Combine(outFolder, "entities.json");
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(entitiesJsonPath, JsonSerializer.Serialize(docReports, opts));

            Console.WriteLine($"Fertig. {docReports.Count} Dokumente verarbeitet. Entitaeten-Report: {entitiesJsonPath}");
            return 0;
        }

        private sealed class DocReport
        {
            public string Document { get; set; } = string.Empty;
            public string AnonTextFile { get; set; } = string.Empty;
            public int OriginalLength { get; set; }
            public int AnonymizedLength { get; set; }
            public List<EntityDto> Entities { get; set; } = new();
        }

        private sealed class EntityDto
        {
            public string Text { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Alias { get; set; } = string.Empty;
        }
    }
}
