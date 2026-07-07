namespace BTCPayApp.Core.Models;

public record PreflightCheck(string Label, bool Passed);

public record PreflightResult(bool AllPassed, bool NotApplicable, IReadOnlyList<PreflightCheck> Checks)
{
    public static PreflightResult Skipped()
        => new(true, true, Array.Empty<PreflightCheck>());
}
