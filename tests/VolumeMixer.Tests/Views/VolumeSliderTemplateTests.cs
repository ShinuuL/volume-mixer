using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Threading;

namespace VolumeMixer.Tests.Views;

/// <summary>
/// Verifica que o ControlTemplate customizado do VolumeSlider (em Styles.xaml)
/// expõe o Track como PART_Track, permitindo que o Slider configure
/// Value/Minimum/Maximum automaticamente (mecanismo do WPF via OnApplyTemplate).
/// </summary>
public class VolumeSliderTemplateTests
{
    private static readonly string SliderTemplateXaml = """
        <Slider xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Minimum="0" Maximum="100" Value="42">
          <Slider.Template>
            <ControlTemplate TargetType="Slider">
              <Grid VerticalAlignment="Center">
                <Track x:Name="PART_Track" Height="4">
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Command="Slider.DecreaseLarge" Focusable="False">
                      <RepeatButton.Template>
                        <ControlTemplate TargetType="RepeatButton">
                          <Border Background="Red" CornerRadius="2" />
                        </ControlTemplate>
                      </RepeatButton.Template>
                    </RepeatButton>
                  </Track.DecreaseRepeatButton>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Command="Slider.IncreaseLarge" Focusable="False">
                      <RepeatButton.Template>
                        <ControlTemplate TargetType="RepeatButton">
                          <Border Background="Gray" CornerRadius="2" />
                        </ControlTemplate>
                      </RepeatButton.Template>
                    </RepeatButton>
                  </Track.IncreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb Focusable="False">
                      <Thumb.Template>
                        <ControlTemplate TargetType="Thumb">
                          <Ellipse Width="14" Height="14" Fill="Blue" />
                        </ControlTemplate>
                      </Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                </Track>
              </Grid>
            </ControlTemplate>
          </Slider.Template>
        </Slider>
        """;

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw error;
    }

    [Fact]
    public void Slider_com_template_customizado_configura_o_Track()
    {
        RunOnSta(() =>
        {
            var app = Application.Current ?? new Application();
            var slider = (Slider)XamlReader.Parse(SliderTemplateXaml);

            // Adiciona a uma janela e processa o dispatcher para que o
            // OnApplyTemplate seja chamado e o Track configurado.
            var window = new Window { Content = slider };
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var track = slider.Template.FindName("PART_Track", slider) as Track;
            Assert.NotNull(track);
            Assert.Equal(0, track.Minimum);
            Assert.Equal(100, track.Maximum);
            Assert.Equal(42, track.Value);

            window.Close();
        });
    }
}
