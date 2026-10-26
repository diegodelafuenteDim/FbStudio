using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FbStudio.App.Resources;
using FbStudio.Core.Metadata;

namespace FbStudio.App.Views;

/// <summary>«Script de la base…»: qué incluir. Devuelve las opciones elegidas, o null si se cancela.</summary>
public sealed class SchemaScriptDialog : Window
{
    /// <param name="areas">Las áreas de la base: se puede limitar el script a una.</param>
    public SchemaScriptDialog(string database, SchemaScriptOptions current, IReadOnlyList<string>? areas = null)
    {
        areas ??= [];
        Title = Strings.SchemaScriptDialogTitle;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        CheckBox Box(string text, bool value) => new() { Content = text, IsChecked = value, Margin = new Thickness(0, 2) };
        var domains = Box(Strings.SchemaScriptDomains, current.Domains);
        var generators = Box(Strings.SchemaScriptGenerators, current.Generators);
        var values = Box(Strings.SchemaScriptGeneratorValues, current.GeneratorValues);
        values.Margin = new Thickness(24, 0, 0, 2);
        generators.IsCheckedChanged += (_, _) => values.IsEnabled = generators.IsChecked == true;
        var exceptions = Box(Strings.SchemaScriptExceptions, current.Exceptions);
        var tables = Box(Strings.SchemaScriptTables, current.Tables);
        var views = Box(Strings.SchemaScriptViews, current.Views);
        var procedures = Box(Strings.SchemaScriptProcedures, current.Procedures);
        var functions = Box(Strings.SchemaScriptFunctions, current.Functions);
        var packages = Box(Strings.SchemaScriptPackages, current.Packages);
        var triggers = Box(Strings.SchemaScriptTriggers, current.Triggers);
        var roles = Box(Strings.SchemaScriptRoles, current.Roles);
        var privileges = Box(Strings.SchemaScriptPrivileges, current.Privileges);

        // Toda la base o un área (si la base tiene).
        var scope = new ComboBox
        {
            ItemsSource = new[] { Strings.SchemaScriptWholeDatabase }.Concat(areas.Select(a => string.Format(Strings.SchemaScriptOnlyArea, a))).ToList(),
            SelectedIndex = current.Area is { } chosen && areas.ToList().IndexOf(chosen) is var at and >= 0 ? at + 1 : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsVisible = areas.Count > 0,
        };
        ToolTip.SetTip(scope, Strings.SchemaScriptAreaTip);

        var ok = new Button { Content = Strings.SchemaScriptGenerate, IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = Strings.Cancel, IsCancel = true };
        ok.Click += (_, _) => Close(new SchemaScriptOptions
        {
            Domains = domains.IsChecked == true,
            Generators = generators.IsChecked == true,
            GeneratorValues = values.IsChecked == true,
            Exceptions = exceptions.IsChecked == true,
            Tables = tables.IsChecked == true,
            Views = views.IsChecked == true,
            Procedures = procedures.IsChecked == true,
            Functions = functions.IsChecked == true,
            Packages = packages.IsChecked == true,
            Triggers = triggers.IsChecked == true,
            Roles = roles.IsChecked == true,
            Privileges = privileges.IsChecked == true,
            Area = scope.SelectedIndex > 0 ? areas[scope.SelectedIndex - 1] : null,
        });
        cancel.Click += (_, _) => Close(null);

        var left = new StackPanel { Children = { domains, generators, values, exceptions, roles, privileges } };
        var right = new StackPanel { Children = { tables, views, procedures, functions, packages, triggers } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = string.Format(Strings.SchemaScriptDialogText, database), TextWrapping = TextWrapping.Wrap },
                scope,
                grid,
                new TextBlock { Text = Strings.SchemaScriptDialogNote, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { ok, cancel } },
            },
        };
    }
}
