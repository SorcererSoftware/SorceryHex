using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

// based on Ethan Schoonover's precision colors for machines and people. http://ethanschoonover.com/solarized
namespace Solarized {

   public class Colors {
      static Color color(int value) {
         var bytes = BitConverter.GetBytes(value);
         return Color.FromArgb(0xFF, bytes[2], bytes[1], bytes[0]);
      }

      public static readonly Color base03 = color(0x002b36);
      public static readonly Color base02 = color(0x073642);
      public static readonly Color base01 = color(0x586e75);
      public static readonly Color base00 = color(0x657b83);
      public static readonly Color base0  = color(0x839496);
      public static readonly Color base1  = color(0x93a1a1);
      public static readonly Color base2  = color(0xeee8d5);
      public static readonly Color base3  = color(0xfdf6e3);

      public static readonly Color Yellow = color(0x5b8900);
      public static readonly Color Orange = color(0xcb4b16);
      public static readonly Color Red    = color(0xdc322f);
      public static readonly Color Magenta= color(0xd33682);
      public static readonly Color Violet = color(0x6c71c4);
      public static readonly Color Blue   = color(0x268bd2);
      public static readonly Color Cyan   = color(0x2aa198);
      public static readonly Color Green  = color(0x859900);
   }

   public static class Brushes {
      static Brush brush(Color color) {
         var brush = new SolidColorBrush(color);
         brush.Freeze();
         return brush;
      }

      public static readonly Brush Yellow = brush(Colors.Yellow);
      public static readonly Brush Orange = brush(Colors.Orange);
      public static readonly Brush Red    = brush(Colors.Red);
      public static readonly Brush Magenta= brush(Colors.Magenta);
      public static readonly Brush Violet = brush(Colors.Violet);
      public static readonly Brush Blue   = brush(Colors.Blue);
      public static readonly Brush Cyan   = brush(Colors.Cyan);
      public static readonly Brush Green  = brush(Colors.Green);
   }

   public class Theme : Colors {
      #region Constants

      public static readonly string Info = "http://ethanschoonover.com/solarized";

      #endregion

      public static Theme Instance { get { return Application.Current.Resources["Theme"] as Theme; } }

      #region Main Brushes

      readonly SolidColorBrush _emphasis = new SolidColorBrush();
      readonly SolidColorBrush _primary = new SolidColorBrush();
      readonly SolidColorBrush _secondary = new SolidColorBrush();
      readonly SolidColorBrush _backlight = new SolidColorBrush();
      readonly SolidColorBrush _background = new SolidColorBrush();

      public Brush Emphasis { get { return _emphasis; } }
      public Brush Primary { get { return _primary; } }
      public Brush Secondary { get { return _secondary; } }
      public Brush Backlight { get { return _backlight; } }
      public Brush Background { get { return _background; } }

      #endregion

      #region Variant Info

      public enum Variant { Dark, Light }

      Variant _variant;

      Color current(Color a, Color b) { return _variant == Variant.Light ? a : b; }

      public Variant CurrentVariant {
         get { return _variant; }
         set {
            _variant = value;
            _emphasis.Color = current(base01, base1);
            _primary.Color = current(base00, base0);
            _secondary.Color = current(base1, base01);
            _backlight.Color = current(base2, base02);
            _background.Color = current(base3, base03);
         }
      }

      #endregion

      public Theme(Variant variant) { CurrentVariant = variant; }

      public Theme() {
         CurrentVariant = DateTime.Now.Hour < 7 || DateTime.Now.Hour > 18 ? Variant.Dark : Variant.Light;
      }
   }
}
