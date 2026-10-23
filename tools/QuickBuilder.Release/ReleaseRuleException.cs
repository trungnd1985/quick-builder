namespace QuickBuilder.Release;

/// <summary>
/// A release rule failed. The message is printed as is and names the file, section or version
/// at fault; the command exits with <see cref="Program.RuleFailed"/>.
/// </summary>
internal sealed class ReleaseRuleException(string message) : Exception(message);
