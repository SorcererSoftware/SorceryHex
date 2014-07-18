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
   }

   public class Segment : ISegment {
      readonly byte[] _data;

      public bool HasLength { get { return false; } }
      public int Length { get { return -1; } }
      public int Location { get; private set; }
      public byte this[int index] { get { return _data[Location + index]; } }

      public Segment(byte[] data, int location) { _data = data; Location = location; }

      public int Read(int offset, int length) { return _data.ReadData(length, Location + offset); }
      public void Write(int offset, int length, int value) {
         while (length > 0) {
            _data[Location + offset] = (byte)value;
            value >>= 8;
            offset++;
         }
      }
      public ISegment Inner(int offset) { return new Segment(_data, Location + offset); }
      public ISegment Follow(int offset) { return null; }
   }
}
