using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex {
   public interface IElementFactory {
      int Length { get; }
      IEnumerable<FrameworkElement> CreateElements(int start, int length);
      void Recycle(FrameworkElement element);
   }

   class DataHolder : IElementFactory {
      readonly byte[] _data;
      readonly Queue<Path> _recycles = new Queue<Path>();

      public DataHolder(byte[] data) { _data = data; }
      public int Length { get { return _data.Length; } }
      public IEnumerable<FrameworkElement> CreateElements(int start, int length) {
         Debug.Assert(length < 0x20 * 0x40);
         return Enumerable.Range(start, length).Select(i => {
            var path = UsePath();
            if (i < 0 || i >= Length) { path.Data = null; return path; }

            bool lightweight = _data[i] == 0xFF || _data[i] == 0x00;
            path.Data = Utils.ByteFlyweights[_data[i]];
            path.Fill = lightweight ? Solarized.Theme.Instance.Secondary : Solarized.Theme.Instance.Primary;
            path.Opacity = lightweight ? .75 : 1;
            return path;
         });
      }
      public void Recycle(FrameworkElement element) {
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
      static readonly Geometry LeftArrow  = Geometry.Parse("m0,0 l0,2 -1,-1 z");
      static readonly Geometry RightArrow = Geometry.Parse("m0,0 l0,2  1,-1 z");
      readonly IElementFactory _base;
      readonly byte[] _data;
      readonly IList<int> _pointers = new List<int>();
      readonly Queue<Path> _recycles = new Queue<Path>();

      public int Length { get { return _data.Length; } }
      public GbaDataFormatter(IElementFactory fallback, byte[] data) {
         _data = data;
         _base = fallback;
         LoadPointers();
      }

      public IEnumerable<FrameworkElement> CreateElements(int start, int length) {
         var pointerIndex = FindPointersInRange(start, length);

         var list = new List<FrameworkElement>();

         for (int i = 0; i < length;) {
            int loc = start+i;
            if (pointerIndex >= _pointers.Count) {
               list.AddRange(_base.CreateElements(loc, length - i));
               i = length;
            } else if (_pointers[pointerIndex] <= loc) {
               list.AddRange(CreatePointerElements(loc, _pointers[pointerIndex] + 4 - loc));
               i += _pointers[pointerIndex] + 4 - loc;
               pointerIndex++;
            } else {
               list.AddRange(_base.CreateElements(loc, Math.Min(_pointers[pointerIndex] - loc, length - i)));
               i += _pointers[pointerIndex] - loc;
            }
         }

         return list.Take(length);
      }
      public void Recycle(FrameworkElement element) {
         if (element.Tag == this) {
            _recycles.Enqueue((Path)element);
         } else {
            _base.Recycle(element);
         }
      }

      /// <summary>
      /// Slightly Dumb: Might need more context.
      /// But ok for an initial sweep.
      /// </summary>
      void LoadPointers() {
         _pointers.Clear();
         var end = Length - 3;
         for (int i = 0; i < end; i++) {
            if (_data[i + 3] != 0x08) continue;
            _pointers.Add(i);
            i += 3;
         }
      }

      int FindPointersInRange(int start, int length) {
         // binary search for the start point in the list
         int pointerStartIndex = 0, pointerEndIndex = _pointers.Count - 1;
         while (pointerStartIndex < pointerEndIndex) {
            int guessIndex = (pointerEndIndex + pointerStartIndex) / 2;
            if (_pointers[guessIndex] < start - 3) pointerStartIndex = guessIndex + 1;
            else if (_pointers[guessIndex] >= start - 3 && _pointers[guessIndex] <= start) return guessIndex;
            else pointerEndIndex = guessIndex - 1;
         }
         while (pointerStartIndex < _pointers.Count && _pointers[pointerStartIndex] < start - 3) pointerStartIndex++;
         return pointerStartIndex;
      }

      IEnumerable<FrameworkElement> CreatePointerElements(int start, int length) {
         int pointerStart = start + length - 4;
         int value = _data[pointerStart];
         value |= _data[pointerStart + 1] << 0x08;
         value |= _data[pointerStart + 2] << 0x10;
         var str = value.ToHexString();
         while (str.Length < 6) str = "0" + str;

         var leftEdge = UsePath();
         leftEdge.Data = LeftArrow;

         var data1 = UsePath();
         data1.Data = str.Substring(0, 3).ToGeometry();

         var data2 = UsePath();
         data2.Data = str.Substring(3).ToGeometry();

         var rightEdge = UsePath();
         rightEdge.Data = RightArrow;

         var set = new[] { leftEdge, data1, data2, rightEdge };
         foreach (var element in set.Take(4 - length)) Recycle(element);
         return set.Skip(4 - length);
      }

      Path UsePath() {
         return _recycles.Count > 0 ? _recycles.Dequeue() : new Path {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            Fill = Solarized.Brushes.Red,
            Margin = new Thickness(1),
            Tag = this
         };
      }
   }
}
