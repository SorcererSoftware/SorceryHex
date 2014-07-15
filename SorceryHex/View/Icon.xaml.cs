using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace SorceryHex {
   /// <summary>
   /// Interaction logic for Icon.xaml
   /// </summary>
   public partial class Icon : UserControl {
      public Icon() {
         InitializeComponent();
         MouseDown += (sender, e) => Animate();
      }
      public void Animate() {
         length.Animate(0, 40);
         spin.Animate(0, -90);
         size.Animate(64, 32);
      }
   }
   public static class AnimationExtensions {
      public static void Animate(this FrameworkElement element, double start, double end) {
         var animation = new DoubleAnimation(start, end, new Duration(TimeSpan.FromSeconds(2)));
         animation.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .25 };
         element.BeginAnimation(Slider.ValueProperty, animation);
      }
   }
}
