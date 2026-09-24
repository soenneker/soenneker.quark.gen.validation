// This source is appended to the consuming compilation only when a typed range is used.
using System;
namespace Soenneker.Quark.Gen.Validation.BuildTasks;
internal static class TypedRangeSource
{
    internal const string Text = """
namespace Soenneker.Quark.Generated {
internal sealed class TypedRangeAttribute<T> : global::System.ComponentModel.DataAnnotations.RangeAttribute where T : global::System.IComparable {
 private readonly global::System.ComponentModel.TypeConverter _converter;
 private readonly string _minimumText, _maximumText;
 private global::System.IComparable? _minimum, _maximum;
 internal TypedRangeAttribute(global::System.ComponentModel.TypeConverter converter, string minimum, string maximum) : base(0, 1) {
  _converter = converter; _minimumText = minimum; _maximumText = maximum;
 }
 private void EnsureLimits() {
  if (_minimum is not null) return;
  var culture = ParseLimitsInInvariantCulture ? global::System.Globalization.CultureInfo.InvariantCulture : global::System.Globalization.CultureInfo.CurrentCulture;
  var min = (global::System.IComparable)_converter.ConvertFrom(null, culture, _minimumText)!;
  var max = (global::System.IComparable)_converter.ConvertFrom(null, culture, _maximumText)!;
  var order = min.CompareTo(max);
  if (order > 0 || (order == 0 && (MinimumIsExclusive || MaximumIsExclusive))) throw new global::System.InvalidOperationException("Invalid range limits.");
  _minimum = min; _maximum = max;
 }
 public override bool IsValid(object? value) {
  EnsureLimits();
  if (value is null or string { Length: 0 }) return true;
  object? converted;
  try { converted = value.GetType() == typeof(T) ? value : _converter.ConvertFrom(null, ConvertValueInInvariantCulture ? global::System.Globalization.CultureInfo.InvariantCulture : global::System.Globalization.CultureInfo.CurrentCulture, value); }
  catch (global::System.FormatException) { return false; }
  catch (global::System.InvalidCastException) { return false; }
  catch (global::System.NotSupportedException) { return false; }
  return (MinimumIsExclusive ? _minimum!.CompareTo(converted) < 0 : _minimum!.CompareTo(converted) <= 0) &&
         (MaximumIsExclusive ? _maximum!.CompareTo(converted) > 0 : _maximum!.CompareTo(converted) >= 0);
 }
 public override string FormatErrorMessage(string name) {
  EnsureLimits();
  return string.Format(global::System.Globalization.CultureInfo.CurrentCulture, ErrorMessageString, name, _minimum, _maximum);
 }
}
}
""";
}
