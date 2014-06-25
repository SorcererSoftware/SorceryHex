using Microsoft.Scripting.Hosting;
using SorceryHex.Gba.Pokemon.DataTypes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SorceryHex.Gba {
   class PCS : IRunParser, IEditor {
      static readonly string[] _pcs = new string[0x100];
      static readonly Geometry[] _pcsVisuals = new Geometry[0x100]; // leaving it null makes it use the default color and visualization
      static readonly IDataRun _stringRun;

      static PCS() {
         Instance = new PCS();
         _stringRun = new VariableLengthDataRun(0xFF, 1, Solarized.Brushes.Violet, _pcsVisuals) {
            Interpret = GetInterpretation,
            Editor = PCS.Instance
         };

         foreach (var line in System.IO.File.ReadAllLines("data\\PCS3-W.ini")) {
            var sanitized = line.Trim();
            if (sanitized.StartsWith("#") || sanitized.Length == 0) continue;
            Debug.Assert(sanitized.StartsWith("0x"));
            var key = (byte)sanitized.Substring(2, 2).ParseAsHex();
            var value = sanitized.Substring(sanitized.IndexOf("'") + 1);
            value = value.Substring(0, value.Length - 1);
            _pcs[key] = value;
            _pcsVisuals[key] = value.ToGeometry();
         }
         _pcsVisuals[0x00] = "".ToGeometry();
         _pcsVisuals[0xFD] = "\\x".ToGeometry();
      }

      public static IDataRun StringRun { get { return _stringRun; } }
      public static PCS Instance { get; private set; }

      IRunStorage _runs;

      public void Load(IRunStorage runs) {
         _runs = runs;
         FindStrings();
      }

      public IEnumerable<int> Find(string term) {
         if (!term.All(s => s == ' ' || _pcs.Contains(new string(s, 1)))) yield break;

         var lower = term.ToLower();
         var upper = term.ToUpper();
         var byteRange = Enumerable.Range(0, 0x100).Select(i => (byte)i);

         // two searchTerms, one with caps and one with lowercase
         byte[] searchTerm1 =
            Enumerable.Range(0, term.Length)
            .Select(i => term[i] == ' ' ? (byte)0x00 : byteRange.First(key => _pcs[key] == lower.Substring(i, 1)))
            .ToArray();

         byte[] searchTerm2 =
            Enumerable.Range(0, term.Length)
            .Select(i => term[i] == ' ' ? (byte)0x00 : byteRange.First(key => _pcs[key] == upper.Substring(i, 1)))
            .ToArray();

         for (int i = 0, j = 0; i < _runs.Data.Length; i++) {
            j = _runs.Data[i] == searchTerm1[j] || _runs.Data[i] == searchTerm2[j] ? j + 1 : 0;
            if (j < searchTerm1.Length) continue;
            yield return i - j + 1;
            j = 0;
         }
      }

      #region Editor

      public void Edit(int location, char c) {
         if (c == ' ') {
            _runs.Data[location] = 0;
            MoveToNext(this, EventArgs.Empty);
            return;
         } else if (c == '\n') {
            _runs.Data[location] = 0;
            MoveToNext(this, EventArgs.Empty);
            return;
         }

         for (int i = 0x00; i <= 0xFF; i++) {
            if (_pcs[i] == null) continue;
            if (_pcs[i][0] != c) continue;
            if (_runs.Data[location] == 0xFF) _runs.Data[location + 1] = 0xFF; // TODO this byte needs to show the update too
            _runs.Data[location] = (byte)i;
            MoveToNext(this, EventArgs.Empty);
            return;
         }
      }

      public void CompleteEdit(int location) { }

      public event EventHandler MoveToNext;

      #endregion

      void FindStrings() {
         int currentLength = 0;
         int currentSkip;
         for (int i = 0x200; i < _runs.Data.Length; i++) {

            // phase one: quickly find something that looks string-like
            currentSkip = 0;
            while (currentLength == 0 && i < _runs.Data.Length && _pcs[_runs.Data[i]] == null) {
               currentSkip = Math.Min(currentSkip + 1, 0x10);
               i += currentSkip;
            }
            if (currentSkip > 0) i -= currentSkip - 1;

            // phase two: read to see if it's a string
            while (i < _runs.Data.Length) {
               if (_pcs[_runs.Data[i]] != null) {
                  currentLength++;
                  i++;
                  continue;
               }
               if (_runs.Data[i] == 0x00 && currentLength > 0) { // accept 0x00 if we've already started
                  currentLength++;
                  i++;
                  continue;
               }
               if (_runs.Data[i] == 0xFD) { // accept 0xFD as the escape character
                  i += 2;
                  currentLength += 2;
                  continue;
               }
               if (_runs.Data[i] == 0xFF && currentLength >= 3) {
                  // if all the characters are the same, don't add the run.
                  int startLoc = i - currentLength;
                  if (!Enumerable.Range(1, currentLength).All(j => _runs.Data[startLoc + j] == _runs.Data[startLoc])) {
                     if (_runs.IsFree(startLoc)) {
                        _runs.AddRun(startLoc, _stringRun);
                     }
                  }
               }
               break;
            }
            currentLength = 0;
         }
      }

      public static string ReadString(byte[] data, int location) {
         string result = string.Empty;
         for (int j = 0; data[location + j] != 0xFF; j++) {
            if (data[location + j] == 0x00) {
               result += " ";
            } else if (data[location + j] == 0xFD) {
               result += "\\x" + Utils.ToHexString(data[location + j + 1]);
               j++;
            } else {
               result += _pcs[data[location + j]];
            }
         }
         return result;
      }

      static FrameworkElement GetInterpretation(byte[] data, int location) {
         var result = ReadString(data, location);
         return new TextBlock { Text = result, Foreground = Solarized.Theme.Instance.Primary, TextWrapping = TextWrapping.Wrap };
      }
   }

   class ScriptedDataTypes : IRunParser, ITypes {
      readonly PointerMapper _mapper;
      readonly ScriptEngine _engine;
      readonly ScriptScope _scope;
      readonly string[] _scripts;

      public ScriptedDataTypes(PointerMapper mapper, ScriptEngine engine, ScriptScope scope, params string[] scriptList) {
         _mapper = mapper; _engine = engine; _scope = scope; _scripts = scriptList;
      }

      public IEnumerable<int> Find(string term) { return null; }

      IRunStorage _runs;
      public void Load(IRunStorage runs) {
         _runs = runs;
         _scope.SetVariable("types", this);
         var dir = AppDomain.CurrentDomain.BaseDirectory + "/pokemon_datatypes/";
         foreach (var script in _scripts) {
            using (AutoTimer.Time("ScriptedDataTypes-" + script)) {
               var source = _engine.CreateScriptSourceFromFile(dir + script);
               source.Execute(_scope);
            }
         }
         _scope.RemoveVariable("types");
      }

      public string Version { get { return Header.GetCode(_runs.Data); } }

      const int MinVariableLength = 10;
      public Pointer FindVariableArray(string generalLayout, ChildReader reader) {
         int stride = generalLayout.Length * 4;

         var addressesList = _mapper.OpenDestinations.ToList();
         var matchingPointers = new List<int>();
         var matchingLayouts = new List<int>();
         var matchingLengths = new List<int>();
         foreach (var address in addressesList) {
            if (address == 0x23EAC8) {
               int x = 7;
            }
            int elementCount1 = 0, elementCount2 = 0;
            while (GeneralMatch(address + elementCount1 * stride, generalLayout)) elementCount1++;
            if (address + stride * elementCount1 >= _runs.Data.Length) continue;
            if (elementCount1 < MinVariableLength) continue;

            var parser = new Parser(_runs, address);
            for (int i = 0; true; i++) {
               reader(parser);
               if (parser.FaultReason != null) break;
               elementCount2++;
            }
            if (elementCount2 < MinVariableLength) continue;
            matchingLayouts.Add(address);
            matchingLengths.Add(elementCount2);
            matchingPointers.Add(_mapper.PointersFromDestination(address).First());
         }

         if (matchingLayouts.Count == 0) {
            Debug.Fail("No layouts matching " + generalLayout);
            return null;
         }

         return FindVariableArray(reader, stride, matchingPointers, matchingLayouts, matchingLengths);
      }

      public Pointer FindVariableArray(byte ender, string generalLayout, ChildReader reader) {
         int stride = generalLayout.Length * 4;

         var addressesList = _mapper.OpenDestinations.ToList();
         var matchingPointers = new List<int>();
         var matchingLayouts = new List<int>();
         var matchingLengths = new List<int>();
         foreach (var address in addressesList) {
            int elementCount = 0;
            while (address + stride * elementCount < _runs.Data.Length && _runs.Data[address + stride * elementCount] != ender) {
               elementCount++;
            }
            if (address + stride * elementCount >= _runs.Data.Length) continue;
            if (elementCount < MinVariableLength) continue;
            if (Enumerable.Range(0, elementCount).Any(i => !GeneralMatch(address + i * stride, generalLayout))) continue;

            var parser = new Parser(_runs, address);
            for (int i = 0; i < elementCount; i++) {
               reader(parser);
               if (parser.FaultReason != null) break;
            }
            if (parser.FaultReason != null) continue;
            matchingLayouts.Add(address);
            matchingLengths.Add(elementCount);
            matchingPointers.Add(_mapper.PointersFromDestination(address).First());
         }

         if (matchingLayouts.Count == 0) {
            Debug.Fail("No layouts matching " + generalLayout);
            return null;
         }

         return FindVariableArray(reader, stride, matchingPointers, matchingLayouts, matchingLengths);
      }

      Pointer FindVariableArray(ChildReader reader, int stride, IList<int> matchingPointers, IList<int> matchingLayouts, IList<int> matchingLengths) {
         var counts = new List<double>();
         for (int i = 0; i < matchingLayouts.Count; i++) {
            var layout = matchingLayouts[i];
            var length = matchingLengths[i];
            int repeatCount = 0;
            byte prev = _runs.Data[layout];
            for (int j = 1; j < length; j++) {
               var current = _runs.Data[layout + j];
               if (prev == current) repeatCount++;
               prev = current;
            }
            counts.Add((double)repeatCount / length);
         }

         var least = counts.Min();
         var index = counts.IndexOf(least);

         var factory = new Factory(_runs, _mapper, matchingLayouts[index]);
         var data = new dynamic[matchingLengths[index]];
         for (int i = 0; i < matchingLengths[index]; i++) {
            reader(factory);
            data[i] = factory.Result;
         }

         int start = matchingLayouts[index], end = matchingLayouts[index] + matchingLengths[index] * stride;
         _mapper.FilterPointer(i => i <= start || i >= end);
         return new Pointer { source = matchingPointers[index], destination = matchingLayouts[index], data = data };
      }

      public Pointer[] FindMany(string generalLayout, ChildReader reader) {
         var matchingPointers = new List<Pointer>();
         var addressesList = _mapper.OpenDestinations.ToList();
         foreach (var address in addressesList) {
            if (!GeneralMatch(address, generalLayout)) continue;
            var parser = new Parser(_runs, address);
            reader(parser);
            if (parser.FaultReason != null) continue;
            var factory = new Factory(_runs, _mapper, address);
            reader(factory);

            var p = new Pointer {
               source = _mapper.PointersFromDestination(address).First(),
               destination = address,
               data = factory.Result
            };
            matchingPointers.Add(p);
         }
         return matchingPointers.ToArray();
      }

      /// <summary>
      /// Given a list of pointers, finds the ones that are pointed to.
      /// Returns the pointers pointing to the list.
      /// </summary>
      public Pointer[] FollowPointersUp(Pointer[] locations) {
         var pointerSet = new List<Pointer>();
         foreach (var destination in locations) {
            var pointers = _mapper.PointersFromDestination(destination.source);
            if (pointers == null || pointers.Length == 0) continue;
            var p = new Pointer {
               source = pointers.First(),
               destination = destination.source,
               data = new AutoArray(_runs.Data, locations, destination.source)
            };
            pointerSet.Add(p);
         }
         return pointerSet.ToArray();
      }

      bool GeneralMatch(int address, string layout) {
         layout = layout.ToLower();
         if (address + layout.Length * 4 > _runs.Data.Length) return false;
         for (int i = 0; i < layout.Length; i++) {
            Debug.Assert(layout[i] == 'p' || layout[i] == 'w');
            if (layout[i] != 'p') continue;
            int value = _runs.Data.ReadData(4, address + i * 4);
            int pointer = _runs.Data.ReadPointer(address + i * 4);
            if (pointer == -1 && value != 0) return false;
         }
         return true;
      }
   }

   public class AutoArray {
      readonly byte[] _data;
      readonly IList<Pointer> _pointers;
      public readonly int destination;
      public AutoArray(byte[] data, IList<Pointer> pointers, int loc) { _data = data; _pointers = pointers; destination = loc; }
      public dynamic this[int i] {
         get {
            var loc = destination + i * 4;
            var r = _pointers.FirstOrDefault(p => p.destination == _data.ReadPointer(loc));
            if (r != null) return r.data;
            return null;
         }
      }
      public int destinationof(int i) {
         var loc = destination + i * 4;
         var r = _pointers.FirstOrDefault(p => p.destination == _data.ReadPointer(loc));
         return r.destination;
      }
   }

   enum Offset { IconImage, IconPalette, IconPaletteIndex }

   class SpeciesElementProvider : IElementProvider {
      public static readonly IElementProvider Instance = new SpeciesElementProvider();
      SpeciesElementProvider() { }

      readonly Queue<Image> _recycles = new Queue<Image>();
      readonly Queue<Rectangle> _empties = new Queue<Rectangle>();
      readonly IDictionary<int, ImageSource> _cache = new Dictionary<int, ImageSource>();

      public bool IsEquivalent(IElementProvider other) { return other == this; }

      public FrameworkElement ProvideElement(ICommandFactory commandFactory, byte[] data, int runStart, int innerIndex, int runLength) {
         if (innerIndex == 1) return ProvideEmpty();
         int index = data.ReadShort(runStart);
         var image = ProvideImage();
         ImageSource source;
         if (_cache.ContainsKey(index)) {
            source = _cache[index];
         } else {
            source = Thumbnails.CropIcon(Thumbnails.GetIcon(data, data.ReadShort(runStart)));
            source.Freeze();
            _cache[index] = source;
         }
         image.Source = source;
         return image;
      }

      public void Recycle(FrameworkElement element) {
         if (element is Image) _recycles.Enqueue((Image)element);
         else if (element is Rectangle) _empties.Enqueue((Rectangle)element);
         else Debug.Fail("Cannot deal with this kind of element.");
      }

      FrameworkElement ProvideEmpty() {
         if (_empties.Count > 0) return _empties.Dequeue();
         return new Rectangle();
      }

      Image ProvideImage() {
         if (_recycles.Count > 0) return _recycles.Dequeue();
         var image = new Image { Width = MainWindow.ElementWidth, Height = MainWindow.ElementHeight };
         Grid.SetColumnSpan(image, 2);
         return image;
      }
   }

   class JumpElementProvider : IElementProvider {
      readonly GeometryElementProvider _provider = new GeometryElementProvider(Utils.ByteFlyweights, Solarized.Brushes.Orange, true);
      readonly ChildJump _jump;
      public JumpElementProvider(ChildJump jump) { _jump = jump; }

      public bool IsEquivalent(IElementProvider other) {
         var that = other as JumpElementProvider;
         if (that == null) return false;
         return _jump == that._jump;
      }

      public FrameworkElement ProvideElement(ICommandFactory commandFactory, byte[] data, int runStart, int innerIndex, int runLength) {
         var element = _provider.ProvideElement(commandFactory, data, runStart, innerIndex, runLength);
         var reader = new Reader(data,runStart);
         int jump = _jump(reader);
         commandFactory.CreateJumpCommand(element, jump);
         return element;
      }

      public void Recycle(FrameworkElement element) { _provider.Recycle(element); }
   }

   class Thumbnails : IRunParser {
      #region Utils

      public static readonly Brush MediaBrush = Solarized.Brushes.Cyan;

      static readonly IDictionary<Offset, int> _fireredLeafgreen = new Dictionary<Offset, int> {
         { Offset.IconImage, 0x138 },
         { Offset.IconPaletteIndex, 0x13C },
         { Offset.IconPalette, 0x140 }
      };

      static readonly IDictionary<Offset, int> _rubySapphire = new Dictionary<Offset, int> {
         { Offset.IconImage, 0x99AA0 },
         { Offset.IconPaletteIndex, 0x99BB0 },
         { Offset.IconPalette, 0x9D53C }
      };

      static readonly IDictionary<string, IDictionary<Offset, int>> _tables = new Dictionary<string, IDictionary<Offset, int>> {
         { "AXVE", _rubySapphire },
         { "AXPE", _rubySapphire },
         { "BPEE", _fireredLeafgreen }, // emerald
         { "BPRE", _fireredLeafgreen },
         { "BPGE", _fireredLeafgreen }
      };

      public static BitmapSource GetIcon(byte[] data, int index) {
         var code = Header.GetCode(data);
         var table = _tables[code];
         int imageOffset = data.ReadPointer(table[Offset.IconImage]);
         int paletteOffset = data.ReadPointer(table[Offset.IconPaletteIndex]);
         int paletteTable = data.ReadPointer(table[Offset.IconPalette]);

         paletteOffset += index;
         imageOffset += index * 4;

         var paletteStart = data.ReadPointer(paletteTable + data[paletteOffset] * 8);
         var imageStart = data.ReadPointer(imageOffset);

         var dataBytes = new byte[0x400];
         Array.Copy(data, imageStart, dataBytes, 0, 0x400);
         var paletteBytes = new byte[0x20];
         Array.Copy(data, paletteStart, paletteBytes, 0, 0x20);
         var palette = new ImageUtils.Palette(paletteBytes);
         int width = 32, height = 64;
         return ImageUtils.Expand16bitImage(dataBytes, palette, width, height);
      }

      public static ImageSource CropIcon(BitmapSource source) {
         int newWidth = MainWindow.ElementWidth, newHeight = MainWindow.ElementHeight;
         int oldWidth = (int)source.Width, oldHeight = (int)source.Height;
         int x = (oldWidth - newWidth) / 2, y = (oldHeight / 2 - newHeight) / 2 + 2;
         return new CroppedBitmap((BitmapSource)source, new Int32Rect(x, y, newWidth, newHeight));
      }

      #endregion

      readonly PointerMapper _mapper;

      public Thumbnails(PointerMapper mapper) { _mapper = mapper; }

      public IEnumerable<int> Find(string term) { return null; }

      public void Load(IRunStorage runs) {
         var data = runs.Data;
         var code = Header.GetCode(data);
         var table = _tables[code];

         int imageOffset = data.ReadPointer(table[Offset.IconImage]);
         _mapper.Claim(runs, imageOffset);

         int index = 0;
         while (data[imageOffset + 3] == 0x08) {
            int i = index; // closure
            var run = new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, MediaBrush), 0x400) {
               Interpret = (d, dex) => {
                  var source = GetIcon(data, i);
                  var image = new Image { Source = source, Width = source.Width, Height = source.Height };
                  return image;
               }
            };

            _mapper.Claim(runs, run, data.ReadPointer(imageOffset));

            imageOffset += 4;
            index++;
         }

         byte[] palettes = new byte[index];
         Array.Copy(data, data.ReadPointer(table[Offset.IconPaletteIndex]), palettes, 0, index);
         int paletteCount = palettes.Max() + 1;
         int pointersToPalettes = data.ReadPointer(table[Offset.IconPalette]);
         var paletteRun = new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, MediaBrush), 0x20) {
            Interpret = (d, dex) => {
               var dataBytes = new byte[0x20];
               Array.Copy(d, dex, dataBytes, 0, dataBytes.Length);
               return Lz.InterpretPalette(dataBytes);
            }
         };
         for (int i = 0; i < paletteCount; i++) _mapper.Claim(runs, paletteRun, data.ReadPointer(pointersToPalettes + i * 8));

         _mapper.Claim(runs, new SimpleDataRun(new GeometryElementProvider(Utils.ByteFlyweights, MediaBrush), index), data.ReadPointer(table[Offset.IconPaletteIndex]));
         _mapper.Claim(runs, data.ReadPointer(table[Offset.IconPalette]));
      }
   }
}
