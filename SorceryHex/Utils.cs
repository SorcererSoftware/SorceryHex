using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SorceryHex {
   public static class Utils {
      public static readonly DependencyProperty CreatorProperty = DependencyProperty.Register("Creator", typeof(object), typeof(FrameworkElement), new PropertyMetadata(null));
      public static void SetCreator(this FrameworkElement element, object creator) { element.SetValue(CreatorProperty, creator); }
      public static object GetCreator(this FrameworkElement element) { return (object)element.GetValue(CreatorProperty); }

      public static readonly Key[] HexKeys = new[] {
         Key.NumPad0, Key.NumPad1, Key.NumPad2, Key.NumPad3, Key.NumPad4,
         Key.NumPad5, Key.NumPad6, Key.NumPad7, Key.NumPad8, Key.NumPad9,
         Key.D0, Key.D1, Key.D2, Key.D3, Key.D4,
         Key.D5, Key.D6, Key.D7, Key.D8, Key.D9,
         Key.A, Key.B, Key.C, Key.D, Key.E, Key.F
      };

      public static IDictionary<Key, char> CharForKey = new Dictionary<Key, char> {
         { Key.OemBackslash, '\\' }, { Key.Divide, '/' },
         { Key.Multiply, '*' },
         { Key.Subtract, '-' }, { Key.Add, '+' },
         { Key.OemPlus, '+' }, { Key.OemMinus, '-' },
         { Key.Space, ' ' }, { Key.Enter, '\n' },
         { Key.Oem7, '\'' }, { Key.Oem3, '`' },
         { Key.OemComma, ',' },
         { Key.OemPeriod, '.' }, { Key.Decimal, '.' },
      };

      public static char? Convert(Key key) {
         if (key >= Key.A && key <= Key.Z) {
            var baseChar = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 'A' : 'a';
            return (char)(key - Key.A + baseChar);
         }
         if (key >= Key.NumPad0 && key <= Key.NumPad9) return (char)(key - Key.NumPad0 + '0');
         if (key >= Key.D0 && key <= Key.D9) {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) {
               return ")!@#$%^&*("[key - Key.D0];
            }
            return (char)(key - Key.D0 + '0');
         }
         if (CharForKey.ContainsKey(key)) return CharForKey[key];
         return null;
      }

      public static readonly string Hex = "0123456789ABCDEF";
      public static readonly Typeface Font = new Typeface("Consolas");
      public static readonly Geometry[] ByteFlyweights =
         Enumerable.Range(0, 0x100).Select(i => {
            var str = Utils.Hex.Substring(i / 0x10, 1) + Utils.Hex.Substring(i % 0x10, 1);
            var geo = str.ToGeometry();
            geo.Freeze();
            return geo;
         }).ToArray();
      public static readonly Geometry[] AsciiFlyweights =
         Enumerable.Range(0, 0x100).Select(i => {
            var geo = new string((char)i, 1).ToGeometry();
            geo.Freeze();
            return geo;
         }).ToArray();
      public static readonly Geometry[] NumericFlyweights =
         Enumerable.Range(0, 0x100).Select(i => {
            var geo = i.ToString().ToGeometry();
            geo.Freeze();
            return geo;
         }).ToArray();
      public static readonly Geometry[] UpperCaseFlyweights =
         Enumerable.Range(0, 'Z' - 'A' + 1).Select(i => {
            var geo = new string((char)('A' + i), 1).ToGeometry();
            geo.Freeze();
            return geo;
         }).ToArray();
      public static readonly Geometry[] LowerCaseFlyweights =
         Enumerable.Range(0, 'z' - 'a' + 1).Select(i => {
            var geo = new string((char)('a' + i), 1).ToGeometry();
            geo.Freeze();
            return geo;
         }).ToArray();

      public static int ReadPointer(this byte[] memory, int offset) {
         if (memory[offset + 3] != 0x08) return -1;
         return (memory[offset + 2] << 16) | (memory[offset + 1] << 8) | memory[offset + 0];
      }

      public static int ReadShort(this byte[] memory, int offset) {
         return (memory[offset + 1] << 8) | memory[offset + 0];
      }

      public static int ReadData(this byte[] memory, int length, int offset) {
         int value = 0;
         while (length > 0) {
            length--;
            value <<= 8;
            value |= memory[offset];
            offset++;
         }
         return value;
      }

      public static Geometry ToGeometry(this string text) {
         var fText = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Font, 15.0, Brushes.Black);
         var geo = fText.BuildGeometry(new Point());
         geo.Freeze();
         return geo;
      }

      public static string ToHexString(this int value) {
         if (value < 0) return "";
         if (value < 16) return Hex.Substring(value, 1);
         return ToHexString(value / 16) + Hex.Substring(value % 16, 1);
      }

      public static int ParseAsHex(this string value) {
         value = value.ToUpper();
         Debug.Assert(value.All(c => Hex.Contains(c)));

         int parsed = 0;
         for (int i = 0; i < value.Length; i++) {
            parsed <<= 4;
            parsed |= Hex.IndexOf(value[i]);
         }
         return parsed;
      }

      public static IEnumerable<T> Where<T>(this UIElementCollection collection, Func<T, bool> condition) {
         var list = new List<T>();
         foreach (T element in collection) if (condition(element)) list.Add(element);
         return list;
      }

      static string GetFile() {
         var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a File to open." };
         dialog.ShowDialog(null);
         return dialog.FileName;
      }

      public static byte[] LoadFile(out string file, string[] args = null) {
         file = args != null && args.Length == 1 ? args[0] : GetFile();
         if (file == null) return null;
         if (!File.Exists(file)) return null;
         file = file.ToLower();

         using (var stream = new FileStream(file, FileMode.Open)) {
            var data = new byte[stream.Length];
            stream.Read(data, 0, (int)stream.Length);
            return data;
         }
      }

      public enum FindOptions { StartOrBefore, StartOrAfter }
      public static int SearchForStartPoint<T>(int start, IList<T> list, Func<T, int> property, FindOptions option) {
         int locStartIndex = 0, locEndIndex = list.Count - 1;
         while (locStartIndex < locEndIndex) {
            int guessIndex = (locEndIndex + locStartIndex) / 2;
            var loc = property(list[guessIndex]);
            if (loc == start) return guessIndex;
            if (loc < start) locStartIndex = guessIndex + 1;
            else locEndIndex = guessIndex - 1;
         }
         while (option == FindOptions.StartOrBefore && locStartIndex > 0 && property(list[locStartIndex]) > start) locStartIndex--;
         while (option == FindOptions.StartOrAfter && locStartIndex < list.Count && property(list[locStartIndex]) < start) locStartIndex++;
         return locStartIndex;
      }
   }
}
