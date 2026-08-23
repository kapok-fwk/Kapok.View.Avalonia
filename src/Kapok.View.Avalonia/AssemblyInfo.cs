using System.Runtime.CompilerServices;

// Lets the unit test project reach CustomDataGrid.TryConvert (private-static clipboard value
// conversion logic, see Controls/CustomDataGrid.cs) without making it public API. This assembly
// is not strong-named (confirmed - no AssemblyOriginatorKeyFile/SignAssembly in the csproj,
// unlike Kapok.View.Wpf's own InternalsVisibleTo, which needs a public key because that assembly
// is signed), so a plain assembly name is enough.
[assembly: InternalsVisibleTo("Kapok.View.Avalonia.UnitTest")]
