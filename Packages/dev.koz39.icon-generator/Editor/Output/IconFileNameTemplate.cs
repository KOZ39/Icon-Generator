using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KOZ39.IconGenerator
{
    internal static class IconFileNameTemplate
    {
        private static readonly string[] _tokenNames =
        {
            "name",
            "resolution",
            "direction",
            "date",
            "time",
        };

        private static readonly Regex _supportedTokenPattern = new(
            $@"\{{({string.Join("|", _tokenNames)})\}}",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        private static readonly Regex _bracedTokenPattern = new(@"\{([^{}]+)\}");

        internal static IEnumerable<(string Token, string Suggestion)> FindUnknownTokens(
            string template
        )
        {
            foreach (
                var token in _bracedTokenPattern
                    .Matches(template ?? "")
                    .Cast<Match>()
                    .Select(match => match.Groups[1].Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            )
            {
                if (_tokenNames.Contains(token, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var normalizedToken = token.ToLowerInvariant();
                var candidates = _tokenNames
                    .Where(name => Math.Abs(name.Length - token.Length) <= 2)
                    .Select(name => (Name: name, Distance: EditDistance(normalizedToken, name)))
                    .Where(item => item.Distance <= 2)
                    .OrderBy(item => item.Distance)
                    .Take(2)
                    .ToArray();
                var suggestion =
                    candidates.Length > 0
                    && (candidates.Length == 1 || candidates[0].Distance < candidates[1].Distance)
                        ? $"{{{candidates[0].Name}}}"
                        : null;
                yield return ($"{{{token}}}", suggestion);
            }
        }

        private static int EditDistance(string value, string candidate)
        {
            var costs = Enumerable.Range(0, candidate.Length + 1).ToArray();

            for (var row = 1; row <= value.Length; row++)
            {
                var diagonal = costs[0];
                costs[0] = row;

                for (var column = 1; column <= candidate.Length; column++)
                {
                    var previous = costs[column];
                    costs[column] = Math.Min(
                        Math.Min(costs[column] + 1, costs[column - 1] + 1),
                        diagonal + (value[row - 1] == candidate[column - 1] ? 0 : 1)
                    );
                    diagonal = previous;
                }
            }

            return costs[candidate.Length];
        }

        internal static string Expand(
            string template,
            IReadOnlyList<GameObject> sources,
            int resolution,
            DateTime timestamp,
            Vector3 cameraRotation,
            GameObject captureTarget = null
        )
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                template = IconGeneratorSettings.DefaultFileNameTemplate;
            }

            cameraRotation = IconGeneratorSettings.NormalizeRotation(cameraRotation);
            string direction = null;

            return _supportedTokenPattern.Replace(
                template,
                match =>
                    match.Groups[1].Value.ToLowerInvariant() switch
                    {
                        "name" => captureTarget != null ? captureTarget.name
                        : sources.Count == 1 ? sources[0].name
                        : sources.Count > 1 ? "Combined"
                        : "Icon",
                        "resolution" => resolution.ToString(CultureInfo.InvariantCulture),
                        "direction" => direction ??= (
                            IconGeneratorSettings.GetCameraPresetName(cameraRotation) ?? "Custom"
                        ).ToLowerInvariant(),
                        "date" => timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                        "time" => timestamp.ToString("HHmmss", CultureInfo.InvariantCulture),
                        _ => match.Value,
                    }
            );
        }
    }
}
