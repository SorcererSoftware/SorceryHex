using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SorceryHex {
   class GbaImages {

      // 10       (header)
      // 05 00 00 (final data is 5 bytes long)
      // 40       (0100 0000 bitfield)
      // 00       (first uncompressed byte)     (00)
      // 10 00    (runLength 1+3, runOffset 0+1)   (00 00 00 00 00)

      public static IList<int> FindLZImages(byte[] memory) {
         var list = new Dictionary<int, bool>();
         for (int offset = 3; offset < memory.Length; offset++) {

            // find a pointer
            if (memory[offset] != 0x08) continue;
            int pointer = memory.ReadPointer(offset - 3);
            if (pointer < 0 || pointer > memory.Length - 4) continue;

            // check for the 0x10 sentinel at the start of all LZ Compressed data
            if (memory[pointer] != 0x10) continue;

            // images will be a multiple of 16*16*2 bytes when decompressed.
            if (memory[pointer + 1] % 0x80 != 0) continue;
            list[pointer] = true;
         }

         return list.Keys.OrderBy(i => i).ToList();
      }

      // start offset is changed to be the next place we think there is an LZ image
      public static void SearchForLZImage(byte[] memory, ref int startOffset) {
         while (true) {
            while (startOffset < memory.Length && memory[startOffset] != 0x10) startOffset++;
            if (startOffset == memory.Length) { startOffset = -1; return; }
            int length = (memory[startOffset + 3] << 16) | (memory[startOffset + 2] << 8) | (memory[startOffset + 1] << 0);
            if (length % 32 != 0) { startOffset++; continue; }
            return;
         }
      }

      /// <summary>
      /// Similar to UnCompressLZ, except it returns only the length of
      /// the compressed data instead of the full uncompressed data set.
      /// </summary>
      public static void CalculateLZSizes(byte[] memory, int offset, out int uncompressed, out int compressed) {
         // all LZ compressed data starts with 0x10
         if (memory[offset] != 0x10) {
            uncompressed = compressed = -1;
            return;
         }

         int length = (memory[offset + 3] << 16) | (memory[offset + 2] << 8) | (memory[offset + 1] << 0);
         uncompressed = 0; compressed = 4;
         offset += 4;

         while (true) {
            // always start with a bitfield
            // it encodes the next 8 steps
            // "1" means its a runlength dictionary compression
            //     and the dictionary is the most recent decompressed data
            // "0" means its decompressed
            // this makes a fully compressed data stream only 12.5% longer than it was when it started (at worst).
            var bitField = memory[offset++];
            compressed++;
            var bits = new[] {
               (bitField & 0x80) != 0,
               (bitField & 0x40) != 0,
               (bitField & 0x20) != 0,
               (bitField & 0x10) != 0,
               (bitField & 0x08) != 0,
               (bitField & 0x04) != 0,
               (bitField & 0x02) != 0,
               (bitField & 0x01) != 0,
            };

            foreach (var bit in bits) {
               if (bit) {
                  // the next two bytes explain the dictionary position/length of the next set of bytes.
                  // aaaa bbbb . bbbb bbbb

                  // aaaa : the runlength of the dictionary encoding. Never less than 3, so the run
                  //        is encoded as 3 smaller (to allow for slightly larger runs).
                  //        possible final values: 3-18
                  // bbbb bbbb bbbb : how far from the end of the stream to start reading for the run.
                  //                  never 0, so the value is encoded as 1 smaller to allow for slightly
                  //                  longer backtracks.
                  //                  possible final values: 1-4096

                  var byte1 = memory[offset++];
                  compressed++;
                  var runLength = (byte1 >> 4) + 3;
                  var runOffset_upper = (byte1 & 0xF) << 8;
                  var runOffset_lower = memory[offset++];
                  compressed++;
                  var runOffset = (runOffset_lower | runOffset_upper) + 1;
                  if (runOffset > uncompressed) {
                     compressed = uncompressed = -1;
                     return;
                  }
                  uncompressed += runLength;
                  if (uncompressed >= length) return;
               } else {
                  uncompressed++;
                  compressed++;
                  offset++;
                  if (uncompressed == length) return;
               }
            }
         }
      }

      public static byte[] UncompressLZ(byte[] memory, int offset) {
         // all LZ compressed data starts with 0x10
         if (memory[offset] != 0x10) return null;
         int length = (memory[offset + 3] << 16) | (memory[offset + 2] << 8) | (memory[offset + 1] << 0);
         var uncompressed = new List<byte>();
         offset += 4;

         while (true) {
            // always start with a bitfield
            // it encodes the next 8 steps
            // "1" means its a runlength dictionary compression
            //     and the dictionary is the most recent decompressed data
            // "0" means its decompressed
            // this makes a fully compressed data stream only 12.5% longer than it was when it started (at worst).
            var bitField = memory[offset++];
            var bits = new[] {
               (bitField & 0x80) != 0,
               (bitField & 0x40) != 0,
               (bitField & 0x20) != 0,
               (bitField & 0x10) != 0,
               (bitField & 0x08) != 0,
               (bitField & 0x04) != 0,
               (bitField & 0x02) != 0,
               (bitField & 0x01) != 0,
            };

            foreach (var bit in bits) {
               if (bit) {
                  // the next two bytes explain the dictionary position/length of the next set of bytes.
                  // aaaa bbbb . bbbb bbbb

                  // aaaa : the runlength of the dictionary encoding. Never less than 3, so the run
                  //        is encoded as 3 smaller (to allow for slightly larger runs).
                  //        possible final values: 3-18
                  // bbbb bbbb bbbb : how far from the end of the stream to start reading for the run.
                  //                  never 0, so the value is encoded as 1 smaller to allow for slightly
                  //                  longer backtracks.
                  //                  possible final values: 1-4096

                  if (offset >= memory.Length) return null;
                  var byte1 = memory[offset++];
                  var runLength = (byte1 >> 4) + 3;
                  var runOffset_upper = (byte1 & 0xF) << 8;
                  var runOffset_lower = memory[offset++];
                  var runOffset = (runOffset_lower | runOffset_upper) + 1;
                  if (runOffset > uncompressed.Count) return null;
                  foreach (var i in Enumerable.Range(0, runLength)) {
                     uncompressed.Add(uncompressed[uncompressed.Count - runOffset]);
                     if (uncompressed.Count == length) return uncompressed.ToArray();
                  }
               } else {
                  uncompressed.Add(memory[offset++]);
                  if (uncompressed.Count == length) return uncompressed.ToArray();
               }
            }
         }
      }
   }
}
