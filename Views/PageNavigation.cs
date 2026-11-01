using FlatOut4SaveEditor.ViewModels;

namespace FlatOut4SaveEditor.Views;

public sealed record PageNavigation(PageViewModel Page, SearchResult? Target, MainPage Shell);

// All fields opened from a search result, filtered to that value.
public sealed record AllFieldsNavigation(MainPage Shell, string Filter);
