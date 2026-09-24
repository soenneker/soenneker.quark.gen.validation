namespace Soenneker.Quark.Gen.Validation.BuildTasks;
internal static class ComparisonSource
{
    internal const string Text = """
namespace Soenneker.Quark.Generated {
internal sealed class ComparisonMessageAttribute(string otherDisplayName) : global::System.ComponentModel.DataAnnotations.ValidationAttribute(DefaultMessage) {
 // Use the framework's localized default message without constructing its reflection-based CompareAttribute.
 private static readonly global::System.Resources.ResourceManager Resources = new("FxResources.System.ComponentModel.Annotations.SR", typeof(global::System.ComponentModel.DataAnnotations.ValidationAttribute).Assembly);
 private static string DefaultMessage() {
  try { return Resources.GetString("CompareAttribute_MustMatch", global::System.Globalization.CultureInfo.CurrentUICulture) ?? "'{0}' and '{1}' do not match."; }
  // NativeAOT may replace the framework's SR accessors with strings and remove its resource manifest.
  catch (global::System.Resources.MissingManifestResourceException) { return "'{0}' and '{1}' do not match."; }
 }
 public override string FormatErrorMessage(string name) => string.Format(global::System.Globalization.CultureInfo.CurrentCulture, ErrorMessageString, name, otherDisplayName);
}
}
""";
}
