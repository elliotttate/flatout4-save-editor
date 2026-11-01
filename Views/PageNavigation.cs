using FlatOut4SaveEditor.ViewModels;

namespace FlatOut4SaveEditor.Views;

public sealed record PageNavigation(PageViewModel Page, SearchResult? Target, MainPage Shell);
