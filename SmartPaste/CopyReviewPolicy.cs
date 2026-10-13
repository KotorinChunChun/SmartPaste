namespace SmartPaste;

internal static class CopyReviewPolicy
{
    public static bool NeedsReview(bool alwaysReview, IReadOnlyList<CopyChoice> choices) =>
        alwaysReview || choices.Count > 1 || choices.Any(choice => choice.Tokens.Count > 1);
}
