using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SorceryHex {
   static class Utils {
      public static readonly string Hex = "0123456789ABCDEF";
      public static Func<A, C> Compose<A, B, C>(this Func<A, B> x, Func<B, C> y) { return a => y(x(a)); }
      public static readonly Typeface Font = new Typeface("Consolas");
      public static readonly Geometry[] ByteFlyweights =
         Enumerable.Range(0, 0x100).Select(i => (byte)i)
         .Select(b => Utils.Hex.Substring(b / 0x10, 1) + Utils.Hex.Substring(b % 0x10, 1))
         .Select(str => str.ToGeometry())
         .Select(geometry => { geometry.Freeze(); return geometry; })
         .ToArray();

      public static int ReadPointer(this byte[] memory, int offset) {
         if (memory[offset + 3] != 0x08) return -1;
         return (memory[offset + 2] << 16) | (memory[offset + 1] << 8) | memory[offset + 0];
      }

      public static int ReadShort(this byte[] memory, int offset) {
         return (memory[offset + 1] << 8) | memory[offset + 0];
      }

      public static Geometry ToGeometry(this string text) {
         return
            new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Font, 15.0, Brushes.Black)
            .BuildGeometry(new Point());
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
         var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Roms (.GBA)|*GBA", Title = "Choose a Rom to open." };
         dialog.ShowDialog(null);
         return dialog.FileName;
      }

      public static byte[] LoadRom(out string file, string[] args = null) {
         file = args != null && args.Length == 1 ? args[0] : GetFile();
         if (file == null) return null;
         if (!File.Exists(file)) return null;

         using (var stream = new FileStream(file, FileMode.Open)) {
            var rom = new byte[stream.Length];
            stream.Read(rom, 0, (int)stream.Length);
            return rom;
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