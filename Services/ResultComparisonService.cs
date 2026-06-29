using AdGroupUserCompare.Models;

namespace AdGroupUserCompare.Services;

public sealed class ResultComparisonService
{
    public IReadOnlyList<GroupComparisonResult> Compare(CompareUsersRequest request)
    {
        var userA = request.UserA.Trim();
        var userB = request.UserB.Trim();

        if (string.IsNullOrWhiteSpace(userA) || string.IsNullOrWhiteSpace(userB))
        {
            throw new InvalidOperationException("Beide Benutzer muessen gesetzt sein.");
        }

        var optionA = ResolveUserKey(userA, request.Results);
        var optionB = ResolveUserKey(userB, request.Results);

        var rowsA = request.Results.Where(result => UserKeysEqual(GetUserKey(result), optionA)).ToList();
        var rowsB = request.Results.Where(result => UserKeysEqual(GetUserKey(result), optionB)).ToList();

        var groups = rowsA.Select(result => result.GroupName)
            .Concat(rowsB.Select(result => result.GroupName))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(groupName => groupName, StringComparer.CurrentCultureIgnoreCase);

        var comparison = new List<GroupComparisonResult>();
        foreach (var groupName in groups)
        {
            var groupRowsA = rowsA.Where(result => string.Equals(result.GroupName, groupName, StringComparison.CurrentCultureIgnoreCase)).ToList();
            var groupRowsB = rowsB.Where(result => string.Equals(result.GroupName, groupName, StringComparison.CurrentCultureIgnoreCase)).ToList();
            var inA = groupRowsA.Count > 0;
            var inB = groupRowsB.Count > 0;

            comparison.Add(new GroupComparisonResult(
                inA && inB ? "Beide" : inA ? "Nur User 1" : "Nur User 2",
                groupName,
                inA ? userA : "",
                inB ? userB : "",
                string.Join(" | ", groupRowsA.Select(result => result.GroupPath).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(path => path)),
                string.Join(" | ", groupRowsB.Select(result => result.GroupPath).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(path => path))));
        }

        return comparison
            .OrderBy(result => result.Status, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(result => result.GroupName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> BuildUserOptions(IEnumerable<AdUserResult> results)
    {
        return results
            .GroupBy(GetUserKey, StringComparer.CurrentCultureIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return BuildDisplayValue(first);
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string ResolveUserKey(string input, IReadOnlyList<AdUserResult> results)
    {
        var normalizedInput = input.Trim();
        var directMatches = results
            .Where(result =>
                UserKeysEqual(GetUserKey(result), normalizedInput)
                || UserKeysEqual(BuildDisplayValue(result), normalizedInput))
            .Select(GetUserKey)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (directMatches.Count == 1)
        {
            return directMatches[0];
        }

        var partialMatches = results
            .Where(result =>
                Contains(result.SamAccountName, normalizedInput)
                || Contains(result.DisplayName, normalizedInput)
                || Contains(result.Mail, normalizedInput)
                || Contains(result.DistinguishedName, normalizedInput)
                || Contains(BuildDisplayValue(result), normalizedInput))
            .Select(GetUserKey)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return partialMatches.Count switch
        {
            1 => partialMatches[0],
            0 => throw new InvalidOperationException($"Benutzer nicht im geladenen Ergebnis gefunden: {input}"),
            _ => throw new InvalidOperationException($"Benutzer ist nicht eindeutig: {input}")
        };
    }

    private static string GetUserKey(AdUserResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.DistinguishedName))
        {
            return result.DistinguishedName;
        }

        return !string.IsNullOrWhiteSpace(result.SamAccountName) ? result.SamAccountName : result.Mail;
    }

    private static string BuildDisplayValue(AdUserResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.SamAccountName) && !string.IsNullOrWhiteSpace(result.DisplayName))
        {
            return $"{result.SamAccountName} - {result.DisplayName}";
        }

        return !string.IsNullOrWhiteSpace(result.SamAccountName) ? result.SamAccountName : result.DistinguishedName;
    }

    private static bool UserKeysEqual(string keyA, string keyB)
    {
        return string.Equals(keyA.Trim(), keyB.Trim(), StringComparison.CurrentCultureIgnoreCase)
            || StartsWithDisplayValue(keyA, keyB)
            || StartsWithDisplayValue(keyB, keyA);
    }

    private static bool StartsWithDisplayValue(string displayValue, string key)
    {
        return displayValue.StartsWith(key + " - ", StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool Contains(string value, string search)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }
}
