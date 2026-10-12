using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;

namespace CodexMarkdownReader.UI
{
    /// <summary>
    /// Control templates for the whole app. Colors come from DynamicResource keys set by
    /// <see cref="ThemeManager"/>, so switching theme restyles every window, menu and tooltip.
    /// </summary>
    internal static class StyleResources
    {
        public static ResourceDictionary Load()
        {
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(Xaml)))
            {
                return (ResourceDictionary)XamlReader.Load(stream);
            }
        }

        internal const string Xaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <Style x:Key='FocusRing'>
    <Setter Property='Control.Template'>
      <Setter.Value>
        <ControlTemplate>
          <Rectangle Margin='-1' RadiusX='6' RadiusY='6' Stroke='{DynamicResource Accent}' StrokeThickness='1.6' SnapsToDevicePixels='True'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Toolbar icon button -->
  <Style x:Key='IconButton' TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource Muted}'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='32'/>
    <Setter Property='Height' Value='32'/>
    <Setter Property='Padding' Value='0'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource FocusRing}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bg' Background='{TemplateBinding Background}' CornerRadius='6' SnapsToDevicePixels='True'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource Hover}'/>
              <Setter Property='Foreground' Value='{DynamicResource Text}'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource Pressed}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.35'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='ToggleIconButton' TargetType='ToggleButton'>
    <Setter Property='Foreground' Value='{DynamicResource Muted}'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='32'/>
    <Setter Property='Height' Value='32'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource FocusRing}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToggleButton'>
          <Border x:Name='bg' Background='{TemplateBinding Background}' CornerRadius='6' SnapsToDevicePixels='True'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource Hover}'/>
              <Setter Property='Foreground' Value='{DynamicResource Text}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource AccentSoft}'/>
              <Setter Property='Foreground' Value='{DynamicResource Accent}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.35'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Text button (dialogs, info bar, start page) -->
  <Style x:Key='TextButton' TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Background' Value='{DynamicResource Surface2}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Border}'/>
    <Setter Property='Height' Value='32'/>
    <Setter Property='MinWidth' Value='72'/>
    <Setter Property='Padding' Value='14,0'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='HorizontalContentAlignment' Value='Center'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource FocusRing}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bg' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6' SnapsToDevicePixels='True'>
            <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center' Margin='{TemplateBinding Padding}' RecognizesAccessKey='True'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource Hover}'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource Pressed}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.4'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='PrimaryButton' TargetType='Button' BasedOn='{StaticResource TextButton}'>
    <Setter Property='Foreground' Value='{DynamicResource AccentText}'/>
    <Setter Property='Background' Value='{DynamicResource Accent}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Accent}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bg' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6' SnapsToDevicePixels='True'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='{TemplateBinding Padding}' RecognizesAccessKey='True'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource AccentHover}'/>
              <Setter TargetName='bg' Property='BorderBrush' Value='{DynamicResource AccentHover}'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='bg' Property='Opacity' Value='0.85'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.4'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='GhostButton' TargetType='Button' BasedOn='{StaticResource TextButton}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderBrush' Value='Transparent'/>
    <Setter Property='MinWidth' Value='0'/>
    <Setter Property='Padding' Value='10,0'/>
  </Style>

  <Style x:Key='CodeCopyButton' TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource Faint}'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Height' Value='26'/>
    <Setter Property='Padding' Value='8,0'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource FocusRing}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bg' Background='{TemplateBinding Background}' CornerRadius='5'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource Hover}'/>
              <Setter Property='Foreground' Value='{DynamicResource Text}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='LinkButton' TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource Link}'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource FocusRing}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <TextBlock x:Name='text' Background='Transparent' Text='{Binding Content, RelativeSource={RelativeSource TemplatedParent}}'/>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='text' Property='TextDecorations' Value='Underline'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Window caption buttons -->
  <Style x:Key='CaptionButton' TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width' Value='46'/>
    <Setter Property='Height' Value='40'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bg' Background='{TemplateBinding Background}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource Hover}'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource Pressed}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='CaptionCloseButton' TargetType='Button' BasedOn='{StaticResource CaptionButton}'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bg' Background='{TemplateBinding Background}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource CloseHover}'/>
              <Setter Property='Foreground' Value='{DynamicResource OnAccent}'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='bg' Property='Opacity' Value='0.8'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Segmented control items (view mode, sidebar tabs) -->
  <Style x:Key='SegmentButton' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='{DynamicResource Muted}'/>
    <Setter Property='Height' Value='26'/>
    <Setter Property='Padding' Value='10,0'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource FocusRing}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Border x:Name='bg' Background='Transparent' BorderBrush='Transparent' BorderThickness='1' CornerRadius='5' SnapsToDevicePixels='True'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter Property='Foreground' Value='{DynamicResource Text}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource SegmentSelected}'/>
              <Setter TargetName='bg' Property='BorderBrush' Value='{DynamicResource Border}'/>
              <Setter Property='Foreground' Value='{DynamicResource Text}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Context menus -->
  <Style TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='FontFamily' Value='Segoe UI, Microsoft YaHei UI, Microsoft YaHei'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Language' Value='zh-CN'/>
    <Setter Property='Grid.IsSharedSizeScope' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Background='{DynamicResource MenuBg}' BorderBrush='{DynamicResource MenuBorder}' BorderThickness='1' CornerRadius='8' Padding='4' Margin='0,0,10,10'>
            <Border.Effect>
              <DropShadowEffect BlurRadius='16' ShadowDepth='3' Direction='270' Opacity='0.22'/>
            </Border.Effect>
            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle' MinWidth='200'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'>
          <Border Height='1' Margin='10,4' Background='{DynamicResource Border}' SnapsToDevicePixels='True'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Padding' Value='8,6,8,6'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Border x:Name='bg' Background='Transparent' CornerRadius='5' Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True'>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='26'/>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto' SharedSizeGroup='MenuGesture'/>
                <ColumnDefinition Width='18'/>
              </Grid.ColumnDefinitions>
              <ContentPresenter x:Name='icon' Grid.Column='0' ContentSource='Icon' HorizontalAlignment='Left' VerticalAlignment='Center'/>
              <Path x:Name='check' Grid.Column='0' Data='M1,5.5 L4.5,9 L11,2' Stroke='{DynamicResource Accent}' StrokeThickness='1.7'
                    StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' HorizontalAlignment='Left' VerticalAlignment='Center' Visibility='Collapsed'/>
              <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
              <TextBlock x:Name='gesture' Grid.Column='2' Text='{TemplateBinding InputGestureText}' Foreground='{DynamicResource Faint}' Margin='28,0,0,0' VerticalAlignment='Center'/>
              <Path x:Name='arrow' Grid.Column='3' Data='M0,0 L4,4 L0,8' Stroke='{DynamicResource Muted}' StrokeThickness='1.3' HorizontalAlignment='Right' VerticalAlignment='Center' Visibility='Collapsed'/>
              <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='6' VerticalOffset='-6' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'
                     IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>
                <Border Background='{DynamicResource MenuBg}' BorderBrush='{DynamicResource MenuBorder}' BorderThickness='1' CornerRadius='8' Padding='4' Margin='0,0,10,10'>
                  <Border.Effect>
                    <DropShadowEffect BlurRadius='16' ShadowDepth='3' Direction='270' Opacity='0.22'/>
                  </Border.Effect>
                  <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle' Grid.IsSharedSizeScope='True' MinWidth='180'/>
                </Border>
              </Popup>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource Hover}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='check' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='HasItems' Value='True'>
              <Setter TargetName='arrow' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Foreground' Value='{DynamicResource Faint}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='FontFamily' Value='Segoe UI, Microsoft YaHei UI, Microsoft YaHei'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Language' Value='zh-CN'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border Background='{DynamicResource MenuBg}' BorderBrush='{DynamicResource MenuBorder}' BorderThickness='1' CornerRadius='6' Padding='9,5' MaxWidth='460'>
            <ContentPresenter>
              <ContentPresenter.Resources>
                <Style TargetType='TextBlock'>
                  <Setter Property='TextWrapping' Value='Wrap'/>
                </Style>
              </ContentPresenter.Resources>
            </ContentPresenter>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Scroll bars: a visible track and a solid thumb that darkens on hover and while dragging -->
  <Style x:Key='ScrollPage' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'>
          <Border Background='Transparent'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='ScrollThumb' TargetType='Thumb'>
    <Setter Property='Background' Value='{DynamicResource ScrollThumb}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Border x:Name='thumb' Background='{TemplateBinding Background}' CornerRadius='4' Margin='3' SnapsToDevicePixels='True'/>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='thumb' Property='Background' Value='{DynamicResource ScrollThumbActive}'/>
            </Trigger>
            <Trigger Property='IsDragging' Value='True'>
              <Setter TargetName='thumb' Property='Background' Value='{DynamicResource ScrollThumbActive}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Background' Value='{DynamicResource ScrollTrack}'/>
    <Setter Property='Width' Value='14'/>
    <Setter Property='MinWidth' Value='14'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='{TemplateBinding Background}'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Style='{StaticResource ScrollPage}' Command='ScrollBar.PageUpCommand'/>
              </Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton>
                <RepeatButton Style='{StaticResource ScrollPage}' Command='ScrollBar.PageDownCommand'/>
              </Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb x:Name='thumb' Style='{StaticResource ScrollThumb}' MinHeight='36'/>
              </Track.Thumb>
            </Track>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='thumb' Property='Background' Value='{DynamicResource ScrollThumbHover}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='14'/>
        <Setter Property='MinHeight' Value='14'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='ScrollBar'>
              <Grid Background='{TemplateBinding Background}'>
                <Track x:Name='PART_Track' IsDirectionReversed='False'>
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Style='{StaticResource ScrollPage}' Command='ScrollBar.PageLeftCommand'/>
                  </Track.DecreaseRepeatButton>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Style='{StaticResource ScrollPage}' Command='ScrollBar.PageRightCommand'/>
                  </Track.IncreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb x:Name='thumb' Style='{StaticResource ScrollThumb}' MinWidth='36'/>
                  </Track.Thumb>
                </Track>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property='IsMouseOver' Value='True'>
                  <Setter TargetName='thumb' Property='Background' Value='{DynamicResource ScrollThumbHover}'/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource FocusRing}'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Border x:Name='box' Width='16' Height='16' CornerRadius='4' BorderThickness='1.3' BorderBrush='{DynamicResource BorderStrong}' Background='{DynamicResource Surface}' VerticalAlignment='Center'>
              <Path x:Name='mark' Data='M3,8 L6.3,11 L12,4.8' Stroke='{DynamicResource AccentText}' StrokeThickness='1.8' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Visibility='Collapsed' Margin='-1,-1,0,0'/>
            </Border>
            <ContentPresenter Margin='8,0,0,0' VerticalAlignment='Center' RecognizesAccessKey='True'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource Accent}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='box' Property='Background' Value='{DynamicResource Accent}'/>
              <Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource Accent}'/>
              <Setter TargetName='mark' Property='Visibility' Value='Visible'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Text inputs -->
  <Style x:Key='InputBox' TargetType='TextBox'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Background' Value='{DynamicResource Surface}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Border}'/>
    <Setter Property='CaretBrush' Value='{DynamicResource Accent}'/>
    <Setter Property='SelectionBrush' Value='{DynamicResource Accent}'/>
    <Setter Property='Padding' Value='6,0'/>
    <Setter Property='Height' Value='30'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border x:Name='border' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6' SnapsToDevicePixels='True'>
            <ScrollViewer x:Name='PART_ContentHost' Focusable='False' HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='border' Property='BorderBrush' Value='{DynamicResource Accent}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='BareTextBox' TargetType='TextBox'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Border Background='{TemplateBinding Background}'>
            <ScrollViewer x:Name='PART_ContentHost' Focusable='False'
                          HorizontalScrollBarVisibility='{TemplateBinding HorizontalScrollBarVisibility}'
                          VerticalScrollBarVisibility='{TemplateBinding VerticalScrollBarVisibility}'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='CodeTextBox' TargetType='TextBox' BasedOn='{StaticResource BareTextBox}'/>

  <!-- Sidebar list -->
  <Style x:Key='SidebarItem' TargetType='ListBoxItem'>
    <Setter Property='Foreground' Value='{DynamicResource Muted}'/>
    <Setter Property='FocusVisualStyle' Value='{StaticResource FocusRing}'/>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBoxItem'>
          <Border x:Name='bg' Background='Transparent' CornerRadius='6' Padding='8,5,8,5' Margin='6,1,6,1' SnapsToDevicePixels='True'>
            <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource Hover}'/>
              <Setter Property='Foreground' Value='{DynamicResource Text}'/>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='bg' Property='Background' Value='{DynamicResource AccentSoft}'/>
              <Setter Property='Foreground' Value='{DynamicResource Text}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='SidebarList' TargetType='ListBox'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Padding' Value='0,4'/>
    <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
    <Setter Property='ItemContainerStyle' Value='{StaticResource SidebarItem}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBox'>
          <ScrollViewer Focusable='False' Padding='{TemplateBinding Padding}' HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto'>
            <ItemsPresenter/>
          </ScrollViewer>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
    }
}
