using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SorceryHex {
   class StringDecoder : IPartialModel, IEditor {
      readonly byte[] _data;
      readonly Queue<Path> _recycles = new Queue<Path>();
      readonly IDictionary<byte, Geometry> _specialCharacters = new Dictionary<byte, Geometry>();
      readonly int Stride;
      int lowerCaseStart = -1, upperCaseStart = -1;

      public StringDecoder(byte[] data, int stride) { _data = data; Stride = stride; }

      #region Partial Model

      public int Length { get { return _data.Length; } }
      public bool CanEdit(ISegment segment) { return true; }
      public IEditor Editor { get { return this; } }
      public void Load(ICommandFactory commander) { }

      public IList<FrameworkElement> CreateElements(ICommandFactory commander, int start, int length) {
         var list = new FrameworkElement[length];

         for (int i = 0; i < length; i++) {
            if ((start + i) % Stride != 0) continue;
            var data = _data[start + i];
            if (lowerCaseStart != -1 && lowerCaseStart <= data && data < lowerCaseStart + 26) {
               list[i] = UseElement(Utils.LowerCaseFlyweights[data - lowerCaseStart]);
            } else if (upperCaseStart != -1 && upperCaseStart <= data && data < upperCaseStart + 26) {
               list[i] = UseElement(Utils.UpperCaseFlyweights[data - upperCaseStart]);
            } else if (_specialCharacters.ContainsKey(data)) {
               list[i] = UseElement(_specialCharacters[data]);
            }
         }

         return list;
      }

      public void Recycle(ICommandFactory commander, FrameworkElement element) { _recycles.Enqueue((Path)element); }
      public bool IsStartOfDataBlock(int location) { return false; }
      public bool IsWithinDataBlock(int location) { return false; }
      public string GetLabel(int location) { return null; }
      public int GetDataBlockStart(int location) { throw new NotImplementedException(); }
      public int GetDataBlockLength(int location) { throw new NotImplementedException(); }
      public FrameworkElement GetInterpretation(int location) { return null; }

      static readonly string UpperCaseAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
      static readonly string LowerCaseAlphabet = UpperCaseAlphabet.ToLower();
      public IEnumerable<int> Find(string term) {
         byte[] searchTerm = null;
         if (term == term.ToUpper() && term.All(UpperCaseAlphabet.Contains)) {
            upperCaseStart = SearchUpperCase(_data, term);
            searchTerm = term.Select(c => (byte)(((int)c) - 'A' + upperCaseStart)).ToArray();
         } else if (term == term.ToLower() && term.All(LowerCaseAlphabet.Contains)) {
            lowerCaseStart = SearchLowerCase(_data, term);
            searchTerm = term.Select(c => (byte)(((int)c) - 'a' + lowerCaseStart)).ToArray();
         }

         if (searchTerm == null) yield break;
         for (int i = 0, j = 0; i < _data.Length; i += Stride) {
            j = _data[i] == searchTerm[j] || _data[i] == searchTerm[j] ? j + 1 : 0;
            if (j < searchTerm.Length) continue;
            yield return i - j + 1;
            j = 0;
         }
      }

      #endregion

      #region Editor

      public FrameworkElement CreateElementEditor(ISegment segment) { return null; }

      public void Edit(ISegment segment, char c) {
         if (segment.Location % Stride != 0) return;
         if (LowerCaseAlphabet.Contains(c)) {
            lowerCaseStart = GetLowerStartFromLowerReference(segment[0], c);
         } else if (UpperCaseAlphabet.Contains(c)) {
            upperCaseStart = GetUpperStartFromUpperReference(segment[0], c);
         } else {
            var geo = new string(c, 1).ToGeometry();
            geo.Freeze();
            _specialCharacters[segment[0]] = geo;
         }
         for (int i = 0; i < Stride; i++) MoveToNext(this, new UpdateLocationEventArgs(segment.Location, segment.Location + 1));
      }

      public void CompleteEdit(ISegment segment) { }

      public event EventHandler<UpdateLocationEventArgs> MoveToNext;

      #endregion

      #region Helpers

      FrameworkElement UseElement(Geometry data) {
         Path element;
         if (_recycles.Count > 0) element = _recycles.Dequeue();
         else element = new Path {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4.0, 3.0, 4.0, 3.0),
            Fill = Solarized.Brushes.Violet
         };

         element.Data = data;
         return element;
      }

      // assuming a-z are continuous, searches for a set of bytes that could represent the same-case string provided.
      // returns the expceted byte for 'a'
      static byte SearchLowerCase(byte[] value, string search) {
         Debug.Assert(search.Length >= 4);
         Debug.Assert(search == search.ToLower());
         return SearchGenericCase(value, search, 'a');
      }

      static byte SearchUpperCase(byte[] value, string search) {
         Debug.Assert(search.Length >= 4);
         Debug.Assert(search == search.ToUpper());
         return SearchGenericCase(value, search, 'A');
      }

      static byte SearchGenericCase(byte[] value, string search, char firstLetterInCase) {
         int[] deltas = new int[search.Length - 1];
         for (int i = 0; i < deltas.Length; i++) deltas[i] = search[i + 1] - search[i];
         int[] success = new int[256];
         for (int i = 0; i < value.Length - deltas.Length; i++) {
            bool fail = false;
            for (int j = 0; j < deltas.Length && !fail; j++) {
               fail |= value[i + j + 1] - value[i + j] != deltas[j];
            }
            if (!fail) success[value[i]]++;
         }

         var firstLetter = Array.IndexOf(success, success.Max());
         var letterDif = search[0] - firstLetterInCase;
         return (byte)(firstLetter - letterDif);
      }

      static byte GetUpperStartFromUpperReference(byte reference, char letter) {
         var letterDif = letter - 'A';
         return (byte)(reference - letterDif);
      }

      static byte GetLowerStartFromLowerReference(byte reference, char letter) {
         var letterDif = letter - 'a';
         return (byte)(reference - letterDif);
      }

      #endregion
   }
}
