using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex {
   public interface IElementFactory {
      int Length { get; }
      IEnumerable<UIElement> CreateElements(int start, int length);
      void Recycle(UIElement element);
   }

   class DataHolder : IElementFactory {
      public static readonly Typeface Font = new Typeface("Consolas");
      public static readonly Geometry[] ByteFlyweights =
         Enumerable.Range(0, 0x100).Select(i => (byte)i)
         .Select(b => Utils.Hex.Substring(b / 0x10, 1) + Utils.Hex.Substring(b % 0x10, 1))
         .Select(str => new FormattedText(str, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Font, 15.0, Brushes.Black))
         .Select(txt => txt.BuildGeometry(new Point()))
         .ToArray();

      readonly byte[] _data;

      readonly Queue<Path> _recycles = new Queue<Path>();

      public DataHolder(byte[] data) { _data = data; }
      public int Length { get { return _data.Length; } }
      public IEnumerable<UIElement> CreateElements(int start, int length) {
         return Enumerable.Range(start, length).Select(i => {
            var path = UsePath();
            if (i < 0 || i >= Length) { path.Data = null; return path; }

            bool lightweight = _data[i] == 0xFF || _data[i] == 0x00;
            path.Data = ByteFlyweights[_data[i]];
            path.Fill = lightweight ? Solarized.Theme.Instance.Secondary : Solarized.Theme.Instance.Primary;
            path.Opacity = lightweight ? .75 : 1;
            return path;
         });
      }
      public void Recycle(UIElement element) {
         _recycles.Enqueue((Path)element);
      }

      Path UsePath() {
         return _recycles.Count > 0 ? _recycles.Dequeue() : new Path {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4.0, 3.0, 4.0, 3.0)
         };
      }
   }

   class GbaDataFormatter : IElementFactory {
      readonly IElementFactory _base;
      readonly byte[] _data;

      public GbaDataFormatter(IElementFactory fallback, byte[] data) {
         _data = data;
         _base = fallback;
         Length = data.Length;
      }

      public int Length { get; private set; }

      public IEnumerable<UIElement> CreateElements(int start, int length) {
         return _base.CreateElements(start, length);
      }

      public void Recycle(UIElement element) {
         _base.Recycle(element);
      }
   }

}
