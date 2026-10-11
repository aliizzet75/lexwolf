using System;
using System.Collections.Generic;
using System.Linq;

namespace LexWolf.Services
{
    /// <summary>
    /// Produktions-Logik für das Mandanten-Dropdown. Isoliert in einem Service,
    /// damit sie direkt von den xUnit-Regressionstests in LexWolf.Tests
    /// abgedeckt wird, ohne WPF-UI aufzubauen.
    /// </summary>
    public static class MandantFilter
    {
        public const string KeinMandantLabel = "— kein Mandant —";
        public const int MaxMandantDropdownItems = 100;

        public static List<string> ApplyFilter(
            List<(string Id, string Name)> mandanten,
            string? filterText)
        {
            var filter = (filterText ?? "").Trim();
            var items = new List<string>();

            if (string.IsNullOrEmpty(filter))
            {
                if (mandanten.Count == 0)
                {
                    items.Add(KeinMandantLabel);
                }
                else
                {
                    items.AddRange(mandanten
                        .Select(m => m.Name)
                        .Take(MaxMandantDropdownItems));
                }
            }
            else
            {
                var gefiltert = mandanten
                    .Where(m => m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.Name)
                    .Take(MaxMandantDropdownItems)
                    .ToList();

                if (gefiltert.Count == 0)
                    items.Add(KeinMandantLabel);

                items.AddRange(gefiltert);
            }

            return items;
        }
    }
}
