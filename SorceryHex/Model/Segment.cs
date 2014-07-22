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
      ISegment Inner(int offset);
      ISegment Follow(int offset);
      ISegment Resize(int length);
   }

   public class Segment : ISegment {
      readonly byte[] _data;

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
      public ISegment Inner(int offset) { return new Segment(_data, Location + offset); }
      public ISegment Follow(int offset) { return null; }
      public ISegment Resize(int length) { throw new NotImplementedException(); }
   }
}
