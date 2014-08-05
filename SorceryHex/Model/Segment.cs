using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SorceryHex {
   public interface ISegment {
      bool HasLength { get; }
      int Length { get; }
      int Location { get; }
      byte this[int index] { get; }

      int Read(int offset, int length);
      void Write(int offset, int length, int value);
      void Append(int length);
      ISegment Inner(int offset);
      ISegment Follow(int offset);
      ISegment Resize(int length);
      ISegment Duplicate(int offset, int length);
   }

   public class Segment : ISegment {
      byte[] _data;

      public bool HasLength { get { return Length != -1; } }
      public int Length { get; private set; }
      public int Location { get; private set; }
      public byte this[int index] { get { return _data[Location + index]; } }

      public Segment(byte[] data, int location) { _data = data; Location = location; Length = -1; }
      public Segment(byte[] data, int location, int length) { _data = data; Location = location; Length = length; }

      public int Read(int offset, int length) { return _data.ReadData(length, Location + offset); }
      public void Write(int offset, int length, int value) {
         while (length > 0) {
            _data[Location + offset] = (byte)value;
            value >>= 8;
            offset++;
            length--;
         }
      }
      public void Append(int length) {
         var old = _data;
         _data = new byte[_data.Length + length];
         Array.Copy(old, _data, old.Length);
         Array.Copy(old, old.Length - length, _data, old.Length, length);
      }
      public ISegment Inner(int offset) { return new Segment(_data, Location + offset); }
      public ISegment Follow(int offset) { return null; }
      public ISegment Resize(int length) { throw new NotImplementedException(); }
      public ISegment Duplicate(int offset, int length) {
         var data = new byte[length];
         Array.Copy(_data, offset, data, 0, length);
         return new Segment(data, 0, length);
      }
   }
}
