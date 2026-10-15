using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Automation;
using ShapePath = System.Windows.Shapes.Path;

namespace Arqyzeng.Revit;

internal sealed partial class PrintWindow
{
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0, 107, 179));
    private static readonly Brush InkBrush = new SolidColorBrush(Color.FromRgb(36, 49, 62));
    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(211, 220, 229));
    private static readonly Brush CanvasBrush = new SolidColorBrush(Color.FromRgb(245, 247, 250));
    private static readonly Brush HoverBrush = new SolidColorBrush(Color.FromRgb(234, 244, 252));

    private static TextBlock CreateSectionTitle(string title) => new()
    {
        Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold,
        Foreground = InkBrush, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 16, 0, 12)
    };

    private static Border CreateCard(UIElement content) => new()
    {
        Background = Brushes.White, BorderBrush = LineBrush, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Padding = new Thickness(16), Child = content
    };

    private static Grid CreateTabGrid()
    {
        var grid = new Grid { Margin = new Thickness(16) };
        // TabItem selection styling must not flow into the page content.
        TextElement.SetForeground(grid, InkBrush);
        TextElement.SetFontWeight(grid, FontWeights.Normal);
        return grid;
    }

    private static void ApplyWindowTheme(Window window)
    {
        window.FontFamily = new FontFamily("Segoe UI");
        window.FontSize = 13;
        window.Foreground = InkBrush;
        window.Background = CanvasBrush;
        window.UseLayoutRounding = true;
        window.Resources[typeof(Button)] = CreateButtonStyle(false);
        window.Resources[typeof(ComboBox)] = CreateComboBoxStyle();
        window.Resources[typeof(TextBox)] = CreateInputStyle(typeof(TextBox));
        window.Resources[typeof(PasswordBox)] = CreateInputStyle(typeof(PasswordBox));
        window.Resources[typeof(ComboBoxItem)] = CreateSelectionItemStyle(typeof(ComboBoxItem));
        window.Resources[typeof(ListBoxItem)] = CreateSelectionItemStyle(typeof(ListBoxItem));
        window.Resources[typeof(ListBox)] = CreateInputStyle(typeof(ListBox));
        window.Resources[typeof(TabControl)] = CreateTabsStyle();
        window.Resources[typeof(TabItem)] = CreateTabStyle();
        window.Resources[typeof(ScrollBar)] = CreateScrollBarStyle();
        window.Resources[typeof(ToolTip)] = CreateToolTipStyle();
        window.Resources[typeof(ScrollViewer)] = CreateScrollViewerStyle();
    }

    private static void ApplyPrimaryButtonStyle(Button button) => button.Style = CreateButtonStyle(true);

    private static Button CreateCompactButton(string label) => new()
    {
        Content = label, Style = CreateButtonStyle(false), Height = 36,
        Padding = new Thickness(12, 4, 12, 4), FontSize = 13
    };

    private void ConfigureUiToolTips()
    {
        _prepareButton.ToolTip = "Cria uma cópia 3D limpa; preserva a vista original e os materiais.";
        _guideButton.ToolTip = "Mostra a área que será capturada sobre a vista do Revit.";
        _showPrompt.ToolTip = "Abre as instruções do render, sem enviar uma solicitação à API.";
        _showPanoramaAiPrompt.ToolTip = "Confira e copie o prompt 360 antes de gerar.";
        _confirmMaterials.ToolTip = "Informe acabamentos conhecidos para orientar a IA, sem alterar o modelo.";
        _history.ToolTip = "Compare referências e renders salvos no histórico local.";
        _saveToHistory.ToolTip = "Registra esta geração no histórico local. A chave API não é salva.";
        _panoramaFineDetails.ToolTip = "Captura em 2× e reduz para o tamanho final. Usa mais memória e tempo.";
        _buildPanorama.ToolTip = "Monta o PNG 2:1 a partir de panorama.json e das seis faces salvas.";
        _viewPanorama.ToolTip = "Abre um panorama 2:1 para olhar ao redor com o mouse.";
        _loadPanoramaMaterials.ToolTip = "Carrega materiais candidatos do panorama.json na mesma pasta do PNG.";
        _checkPanoramaAiSeam.ToolTip = "Confira a continuidade onde as duas bordas do panorama se encontram.";
        _repairPanoramaSeam.ToolTip = "Propõe um reparo local por uma nova chamada paga. Confira antes de aplicar.";
        _panoramaAiKey.ToolTip = "A chave fica apenas nesta sessão e não é salva nos arquivos.";
    }

    private static Binding TemplateBinding(string path) => new(path)
    {
        RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
    };

    private static Style CreateInputStyle(System.Type type)
    {
        var style = new Style(type);
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.ForegroundProperty, InkBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, LineBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 3, 8, 3)));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 14d));
        if (type == typeof(TextBox) || type == typeof(PasswordBox))
        {
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36d));
            var border = TemplateBorder();
            var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            border.AppendChild(host);
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(type) { VisualTree = border }));
        }
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, AccentBrush));
        if (type == typeof(TextBox) || type == typeof(PasswordBox))
            focus.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(249, 252, 255))));
        style.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55));
        disabled.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Arrow));
        style.Triggers.Add(disabled);
        return style;
    }

    private static Style CreateButtonStyle(bool primary)
    {
        var style = CreateInputStyle(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, primary ? AccentBrush : Brushes.White));
        style.Setters.Add(new Setter(Control.ForegroundProperty, primary ? Brushes.White : InkBrush));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36d));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 4)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 2, 10, 2)));
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding());
        label.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        label.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        style.Setters.Add(new Setter(ContentControl.ContentTemplateProperty, new DataTemplate { VisualTree = label }));
        var border = TemplateBorder();
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        border.AppendChild(content);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = border }));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, primary
            ? new SolidColorBrush(Color.FromRgb(0, 87, 150)) : HoverBrush));
        style.Triggers.Add(hover);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Control.BackgroundProperty, primary
            ? new SolidColorBrush(Color.FromRgb(0, 68, 120)) : LineBrush));
        style.Triggers.Add(pressed);
        return style;
    }

    // Native ComboBox selection/keyboard behavior with a white, programmatic template.
    private static Style CreateComboBoxStyle()
    {
        var style = CreateInputStyle(typeof(ComboBox));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36d));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        var root = new FrameworkElementFactory(typeof(Grid));
        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.SetValue(UIElement.FocusableProperty, false);
        toggle.SetValue(ButtonBase.ClickModeProperty, ClickMode.Press);
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay
        });
        var toggleBorder = new FrameworkElementFactory(typeof(Border));
        toggleBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        toggleBorder.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ComboBox), 1) });
        toggleBorder.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ComboBox), 1) });
        toggleBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        toggle.SetValue(Control.TemplateProperty, new ControlTemplate(typeof(ToggleButton)) { VisualTree = toggleBorder });
        root.AppendChild(toggle);
        var arrow = ChevronFactory("Chevron");
        arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
        root.AppendChild(arrow);
        var selection = new FrameworkElementFactory(typeof(ContentPresenter));
        selection.SetBinding(ContentPresenter.ContentProperty, TemplateBinding("SelectionBoxItem"));
        selection.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding("SelectionBoxItemTemplate"));
        selection.SetValue(FrameworkElement.MarginProperty, new Thickness(9, 0, 28, 0));
        selection.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        selection.SetValue(UIElement.IsHitTestVisibleProperty, false);
        root.AppendChild(selection);
        var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetBinding(Popup.IsOpenProperty, TemplateBinding("IsDropDownOpen"));
        popup.SetBinding(Popup.PlacementTargetProperty, new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var popupBorder = new FrameworkElementFactory(typeof(Border));
        popupBorder.SetValue(Border.BackgroundProperty, Brushes.White);
        popupBorder.SetValue(Border.BorderBrushProperty, LineBrush);
        popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
        popupBorder.SetBinding(FrameworkElement.MinWidthProperty, TemplateBinding("ActualWidth"));
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetBinding(FrameworkElement.MaxHeightProperty, TemplateBinding("MaxDropDownHeight"));
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        popupBorder.AppendChild(scroll);
        popup.AppendChild(popupBorder);
        root.AppendChild(popup);
        var template = new ControlTemplate(typeof(ComboBox)) { VisualTree = root };
        var open = new Trigger { Property = ComboBox.IsDropDownOpenProperty, Value = true };
        open.Setters.Add(new Setter(Control.BorderBrushProperty, AccentBrush));
        open.EnterActions.Add(RotateChevron("Chevron", 180));
        open.ExitActions.Add(RotateChevron("Chevron", 0));
        template.Triggers.Add(open);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BorderBrushProperty, AccentBrush));
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(249, 252, 255))));
        style.Triggers.Add(hover);
        return style;
    }

    private static FrameworkElementFactory TemplateBorder()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetBinding(Border.BackgroundProperty, TemplateBinding("Background"));
        border.SetBinding(Border.BorderBrushProperty, TemplateBinding("BorderBrush"));
        border.SetBinding(Border.BorderThicknessProperty, TemplateBinding("BorderThickness"));
        border.SetBinding(Border.PaddingProperty, TemplateBinding("Padding"));
        return border;
    }

    private static FrameworkElementFactory ChevronFactory(string name)
    {
        var path = new FrameworkElementFactory(typeof(ShapePath), name);
        path.SetValue(ShapePath.DataProperty, Geometry.Parse("M 1,1 L 5,5 L 9,1"));
        path.SetValue(ShapePath.StrokeProperty, AccentBrush);
        path.SetValue(ShapePath.StrokeThicknessProperty, 1.6d);
        path.SetValue(ShapePath.StrokeStartLineCapProperty, PenLineCap.Round);
        path.SetValue(ShapePath.StrokeEndLineCapProperty, PenLineCap.Round);
        path.SetValue(ShapePath.StrokeLineJoinProperty, PenLineJoin.Round);
        path.SetValue(FrameworkElement.WidthProperty, 10d);
        path.SetValue(FrameworkElement.HeightProperty, 6d);
        path.SetValue(UIElement.IsHitTestVisibleProperty, false);
        path.SetValue(UIElement.RenderTransformOriginProperty, new Point(.5, .5));
        path.SetValue(UIElement.RenderTransformProperty, new RotateTransform());
        return path;
    }

    private static BeginStoryboard RotateChevron(string name, double angle)
    {
        var animation = new DoubleAnimation(angle, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 150 : 0))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTargetName(animation, name);
        Storyboard.SetTargetProperty(animation, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        return new BeginStoryboard { Storyboard = storyboard, HandoffBehavior = HandoffBehavior.SnapshotAndReplace };
    }

    private static ToggleButton CreateComparisonHandle()
    {
        var style = CreateInputStyle(typeof(ToggleButton));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(Control.BackgroundProperty, HoverBrush));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, "Abrir comparação"));
        style.Setters.Add(new Setter(AutomationProperties.NameProperty, "Abrir comparação"));
        var border = TemplateBorder();
        var arrow = ChevronFactory("HandleChevron");
        arrow.SetValue(UIElement.RenderTransformProperty, new RotateTransform(-90));
        arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(arrow);
        var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = border };
        var expanded = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        expanded.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, "Ocultar comparação"));
        expanded.Setters.Add(new Setter(AutomationProperties.NameProperty, "Ocultar comparação"));
        expanded.EnterActions.Add(RotateChevron("HandleChevron", 90));
        expanded.ExitActions.Add(RotateChevron("HandleChevron", -90));
        template.Triggers.Add(expanded);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BorderBrushProperty, AccentBrush));
        style.Triggers.Add(hover);
        return new ToggleButton
        {
            Name = "ComparisonHandle", Style = style, Width = 28, Height = 56,
            IsChecked = false, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
    }

    private static Style CreateSelectionItemStyle(System.Type type)
    {
        var style = CreateInputStyle(type);
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        var border = TemplateBorder();
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(FrameworkElement.HorizontalAlignmentProperty, TemplateBinding("HorizontalContentAlignment"));
        border.AppendChild(content);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(type) { VisualTree = border }));
        var hover = new Trigger { Property = type == typeof(ComboBoxItem) ? ComboBoxItem.IsHighlightedProperty : UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, HoverBrush));
        style.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(219, 237, 250))));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, AccentBrush));
        style.Triggers.Add(selected);
        return style;
    }

    private static Style CreateTabStyle()
    {
        var style = CreateInputStyle(typeof(TabItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(20, 12, 20, 12)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 2)));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        var border = TemplateBorder();
        var header = new FrameworkElementFactory(typeof(ContentPresenter));
        header.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        border.AppendChild(header);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(TabItem)) { VisualTree = border }));
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, HoverBrush));
        style.Triggers.Add(hover);
        var selected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, AccentBrush));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, AccentBrush));
        style.Triggers.Add(selected);
        return style;
    }

    private static Style CreateTabsStyle()
    {
        var style = new Style(typeof(TabControl));
        var root = new FrameworkElementFactory(typeof(DockPanel));
        var tabs = new FrameworkElementFactory(typeof(TabPanel));
        tabs.SetValue(DockPanel.DockProperty, Dock.Top);
        tabs.SetValue(Panel.IsItemsHostProperty, true);
        tabs.SetValue(KeyboardNavigation.TabIndexProperty, 1);
        root.AppendChild(tabs);
        var content = new FrameworkElementFactory(typeof(ContentPresenter), "PART_SelectedContentHost");
        content.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
        root.AppendChild(content);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(TabControl)) { VisualTree = root }));
        return style;
    }

    private static Style CreateToolTipStyle()
    {
        var style = CreateInputStyle(typeof(ToolTip));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 340d));
        var border = TemplateBorder();
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ToolTip)) { VisualTree = border }));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding());
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        // String data template leaves existing TextBlock tooltips intact.
        style.Resources.Add(new DataTemplateKey(typeof(string)), new DataTemplate(typeof(string)) { VisualTree = text });
        return style;
    }

    private static Style CreateScrollBarStyle()
    {
        var style = new Style(typeof(ScrollBar));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 9d));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        style.Setters.Add(new Setter(Control.BackgroundProperty, CanvasBrush));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetBinding(Border.BackgroundProperty, TemplateBinding("Background"));
        var track = new FrameworkElementFactory(typeof(ModernScrollTrack), "PART_Track");
        foreach (var property in new[] { "Minimum", "Maximum", "Value", "ViewportSize", "Orientation" })
        {
            var target = property switch
            {
                "Minimum" => Track.MinimumProperty, "Maximum" => Track.MaximumProperty,
                "Value" => Track.ValueProperty, "ViewportSize" => Track.ViewportSizeProperty,
                _ => Track.OrientationProperty
            };
            track.SetBinding(target, TemplateBinding(property));
        }
        track.SetValue(Track.IsDirectionReversedProperty, true);
        border.AppendChild(track);
        var template = new ControlTemplate(typeof(ScrollBar)) { VisualTree = border };
        var horizontal = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Horizontal };
        horizontal.Setters.Add(new Setter(FrameworkElement.WidthProperty, double.NaN));
        horizontal.Setters.Add(new Setter(FrameworkElement.HeightProperty, 9d));
        horizontal.Setters.Add(new Setter(Track.IsDirectionReversedProperty, false, "PART_Track"));
        template.Triggers.Add(horizontal);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static Style CreateScrollViewerStyle()
    {
        var style = new Style(typeof(ScrollViewer));
        style.Setters.Add(new Setter(ScrollViewer.PanningModeProperty, PanningMode.VerticalFirst));
        var root = new FrameworkElementFactory(typeof(DockPanel));
        root.SetBinding(Panel.BackgroundProperty, TemplateBinding("Background"));
        FrameworkElementFactory Bar(bool vertical)
        {
            var bar = new FrameworkElementFactory(typeof(ScrollBar), vertical ? "PART_VerticalScrollBar" : "PART_HorizontalScrollBar");
            bar.SetValue(FrameworkElement.StyleProperty, CreateScrollBarStyle());
            bar.SetValue(DockPanel.DockProperty, vertical ? Dock.Right : Dock.Bottom);
            bar.SetValue(ScrollBar.OrientationProperty, vertical ? Orientation.Vertical : Orientation.Horizontal);
            bar.SetBinding(RangeBase.MaximumProperty, TemplateBinding(vertical ? "ScrollableHeight" : "ScrollableWidth"));
            bar.SetBinding(ScrollBar.ViewportSizeProperty, TemplateBinding(vertical ? "ViewportHeight" : "ViewportWidth"));
            var offset = TemplateBinding(vertical ? "VerticalOffset" : "HorizontalOffset");
            offset.Mode = BindingMode.OneWay;
            bar.SetBinding(RangeBase.ValueProperty, offset);
            bar.SetBinding(UIElement.VisibilityProperty, TemplateBinding(vertical ? "ComputedVerticalScrollBarVisibility" : "ComputedHorizontalScrollBarVisibility"));
            return bar;
        }
        root.AppendChild(Bar(false));
        root.AppendChild(Bar(true));
        var presenter = new FrameworkElementFactory(typeof(ScrollContentPresenter), "PART_ScrollContentPresenter");
        presenter.SetBinding(ContentPresenter.ContentProperty, TemplateBinding("Content"));
        presenter.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding("ContentTemplate"));
        presenter.SetBinding(ScrollContentPresenter.CanContentScrollProperty, TemplateBinding("CanContentScroll"));
        presenter.SetBinding(FrameworkElement.MarginProperty, TemplateBinding("Padding"));
        root.AppendChild(presenter);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ScrollViewer)) { VisualTree = root }));
        return style;
    }

    // Each template instance owns its Track children; never share UIElements in setters.
    private sealed class ModernScrollTrack : Track
    {
        public ModernScrollTrack()
        {
            var thumbStyle = new Style(typeof(Thumb));
            thumbStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(158, 176, 192))));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetBinding(Border.BackgroundProperty, TemplateBinding("Background"));
            thumbStyle.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Thumb)) { VisualTree = border }));
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, AccentBrush));
            thumbStyle.Triggers.Add(hover);
            var drag = new Trigger { Property = Thumb.IsDraggingProperty, Value = true };
            drag.Setters.Add(new Setter(Control.BackgroundProperty, AccentBrush));
            thumbStyle.Triggers.Add(drag);
            Thumb = new Thumb { Style = thumbStyle, MinHeight = 18, MinWidth = 7 };
            DecreaseRepeatButton = CreatePageButton();
            IncreaseRepeatButton = CreatePageButton();
            UpdateOrientation();
        }

        private static RepeatButton CreatePageButton()
        {
            var area = new FrameworkElementFactory(typeof(Border));
            area.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var button = new RepeatButton { Focusable = false, IsTabStop = false,
                Template = new ControlTemplate(typeof(RepeatButton)) { VisualTree = area } };
            button.SetBinding(ButtonBase.CommandTargetProperty, new Binding
            { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ScrollBar), 1) });
            return button;
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == OrientationProperty && Thumb is not null) UpdateOrientation();
        }

        private void UpdateOrientation()
        {
            bool vertical = Orientation == Orientation.Vertical;
            Thumb.MinHeight = vertical ? 18 : 7;
            Thumb.MinWidth = vertical ? 7 : 18;
            DecreaseRepeatButton.Command = vertical ? ScrollBar.PageUpCommand : ScrollBar.PageLeftCommand;
            IncreaseRepeatButton.Command = vertical ? ScrollBar.PageDownCommand : ScrollBar.PageRightCommand;
        }
    }
}
