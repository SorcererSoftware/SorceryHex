using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Shapes;

namespace SorceryHex {
   public interface ICommandFactory {
      void CreateJumpCommand(FrameworkElement element, params int[] jumpLocation);
      void RemoveJumpCommand(FrameworkElement element);
      void LinkToInterpretation(FrameworkElement element, FrameworkElement visual);
      void UnlinkFromInterpretation(FrameworkElement element);
   }

   public interface IElementFactory {
      int Length { get; }
      IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length);
      void Recycle(ICommandFactory commander, FrameworkElement element);
      bool IsStartOfDataBlock(int location);
      bool IsWithinDataBlock(int location);
      FrameworkElement GetInterpretation(int location);
      IList<int> Find(string term);
   }

   class RangeChecker : IElementFactory {
      readonly IElementFactory _base;
      readonly Queue<FrameworkElement> _recycles = new Queue<FrameworkElement>();

      public int Length { get { return _base.Length; } }

      public RangeChecker(IElementFactory next) { _base = next; }

      public IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var list = new List<FrameworkElement>();

         int pre = 0, post = 0;
         if (start < 0) { pre = -start; start = 0; length -= pre; }
         if (length < 0) { pre += length; length = 0; }
         if (start + length >= Length) { post = start + length - Length; length = Length - start; }

         if (pre > 0) list.AddRange(Enumerable.Range(0, pre).Select(UseElement));
         list.AddRange(_base.CreateElements(commander, start, length));
         if (post > 0) list.AddRange(Enumerable.Range(0, post).Select(UseElement));

         return list;
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         if (element.Tag == this) _recycles.Enqueue((Rectangle)element);
         else _base.Recycle(commander, element);
      }

      public bool IsStartOfDataBlock(int location) { return _base.IsStartOfDataBlock(location); }
      public bool IsWithinDataBlock(int location) { return _base.IsWithinDataBlock(location); }
      public FrameworkElement GetInterpretation(int location) { return null; }

      public IList<int> Find(string term) { return _base.Find(term); }

      FrameworkElement UseElement(int i) {
         if (_recycles.Count > 0) return _recycles.Dequeue();

         return new Rectangle { Tag = this };
      }
   }

   class DataHolder : IElementFactory {
      readonly byte[] _data;
      readonly Queue<Path> _recycles = new Queue<Path>();

      public DataHolder(byte[] data) { _data = data; }
      public int Length { get { return _data.Length; } }

      public IEnumerable<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         Debug.Assert(length < 0x20 * 0x40);
         return Enumerable.Range(start, length).Select(i => {
            var path = UsePath();
            path.Data = Utils.ByteFlyweights[_data[i]];

            bool lightweight = _data[i] == 0xFF || _data[i] == 0x00;
            path.Fill = lightweight ? Solarized.Theme.Instance.Secondary : Solarized.Theme.Instance.Primary;
            path.Opacity = lightweight ? .75 : 1;

            return path;
         });
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) {
         Debug.Assert(element.Tag == this);
         _recycles.Enqueue((Path)element);
      }

      public bool IsStartOfDataBlock(int location) { return false; }
      public bool IsWithinDataBlock(int location) { return false; }
      public FrameworkElement GetInterpretation(int location) { return null; }

      public IList<int> Find(string term) {
         var sanitized = term.ToUpper().Replace(" ", "");
         if (sanitized.Length % 2 != 0 || !sanitized.All(Utils.Hex.Contains)) return new int[0];
         var list = new List<int>();
         byte[] searchTerm =
            Enumerable.Range(0, sanitized.Length / 2)
            .Select(i => (byte)sanitized.Substring(i * 2, 2).ParseAsHex())
            .ToArray();

         for (int i = 0, j = 0; i < _data.Length; i++) {
            j = _data[i] == searchTerm[j] ? j + 1 : 0;
            if (j < searchTerm.Length) continue;
            list.Add(i - j + 1);
            j = 0;
         }
         return list;
      }

      Path UsePath() {
         if (_recycles.Count > 0) return _recycles.Dequeue();

         return new Path {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4.0, 3.0, 4.0, 3.0),
            Tag = this
         };
      }
   }
}
