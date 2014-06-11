using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SorceryHex.Gba {
   class ImageUtils {

      const int Dpi = 96;
      public static readonly Palette DefaultPalette = new Palette(new[] {
         Colors.Black, Colors.Red, Colors.Orange, Colors.Yellow, 
         Colors.Green, Colors.Blue, Colors.Indigo, Colors.Violet,
         Colors.Gray, Colors.Cyan, Colors.Magenta, Colors.Fuchsia,
         Colors.Brown, Colors.Gold, Colors.Gainsboro, Colors.ForestGreen
      });

      // 10       (header)
      // 05 00 00 (final data is 5 bytes long)
      // 40       (0100 0000 bitfield)
      // 00       (first uncompressed byte)     (00)
      // 10 00    (runLength 1+3, runOffset 0+1)   (00 00 00 00 00)

      public static IList<int> FindLZData(byte[] memory, Func<int, bool> filter) {
         var list = new Dictionary<int, bool>();
         for (int offset = 3; offset < memory.Length; offset++) {

            // find a pointer
            if (memory[offset] != 0x08) continue;
            int pointer = memory.ReadPointer(offset - 3);
            if (pointer < 0 || pointer > memory.Length - 4) continue;

            // check for the 0x10 sentinel at the start of all LZ Compressed data
            if (memory[pointer] != 0x10) continue;

            if (!filter(pointer)) continue;
            list[pointer] = true;
         }

         return list.Keys.OrderBy(i => i).ToList();
      }

      public static IList<int> FindLZImages(byte[] memory) {
         // images will be a multiple of 8*8/2 bytes (0x20) when decompressed.
         return FindLZData(memory, pointer => memory[pointer + 1] % 0x20 == 0);
      }

      public static IList<int> FindLZPalettes(byte[] memory) {
         return FindLZData(memory, pointer => memory[pointer + 1] == 0x20 && memory[pointer + 2] == 0x00 && memory[pointer + 3] == 0x00);
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

      public static void GuessWidthHeight(int dataLength, out int width, out int height) {
         int pixelCount = dataLength / 2;
         height = pixelCount / 0x10;
         width = 1;
         while (height > width && height % 2 == 0) { width *= 2; height /= 2; }
         width *= 0x08; height *= 0x08;
      }

      public static BitmapSource Expand16bitImage(byte[] image16bit, Palette palette32bit, int width, int height) {
         // image16bit is organized as follows:
         // each byte contains 2 pixels, values 0-0xF
         // each set of 8x8 pixels is stored in a block
         // so the image data is 8x8 blocks of 8x8 pixels
         // this might be to helps with compression
         var image32bit = new byte[image16bit.Length * 8];
         for (int i = 0; i < image16bit.Length; i++) {
            int paletteIndex = image16bit[i] & 0xF;
            palette32bit.Write(image32bit, i * 2 + 0, paletteIndex);

            paletteIndex = image16bit[i] >> 4;
            palette32bit.Write(image32bit, i * 2 + 1, paletteIndex);
         }

         return Reorder(image32bit, width, height);
      }

      public class Palette {
         readonly byte[] colors;
         public readonly Color[] Colors = new Color[0x10];

         public Palette(byte[] palette) {
            Debug.Assert(palette.Length == 0x20);
            var length = palette.Length / 2;
            colors = new byte[length * 4];
            for (int i = 0; i < length; i++) {
               var full = palette.ReadShort(i * 2);
               byte blue = (byte)((full & 0x7C00) >> 7);
               byte green = (byte)((full & 0x03E0) >> 2);
               byte red = (byte)((full & 0x001F) << 3);
               Colors[i] = Color.FromArgb(0xFF, red, green, blue);
               colors[i * 4 + 0] = blue;
               colors[i * 4 + 1] = green;
               colors[i * 4 + 2] = red;
               colors[i * 4 + 3] = 0xFF;
            }
         }

         public Palette(Color[] palette) {
            colors = new byte[palette.Length * 4];
            for (int i = 0; i < palette.Length; i++) {
               Colors[i] = palette[i];
               byte blue = palette[i].B;
               byte green = palette[i].G;
               byte red = palette[i].R;
               colors[i * 4 + 0] = blue;
               colors[i * 4 + 1] = green;
               colors[i * 4 + 2] = red;
               colors[i * 4 + 3] = 0xFF;
            }
         }

         public void Write(byte[] image, int pixel, int paletteIndex) {
            image[pixel * 4 + 0] = colors[paletteIndex * 4 + 0];
            image[pixel * 4 + 1] = colors[paletteIndex * 4 + 1];
            image[pixel * 4 + 2] = colors[paletteIndex * 4 + 2];
            image[pixel * 4 + 3] = colors[paletteIndex * 4 + 3];
         }
      }

      static BitmapSource Reorder(byte[] array, int width, int height) {
         // reorder data from blocks into single image
         int blockWrap = width / 8, pixelWrap = 8;
         var imageOutput = new byte[array.Length];
         int j = 0;
         for (int block = 0; block < (width * height / 64); block++) {
            for (int pixel = 0; pixel < 64; pixel++) {
               for (int channel = 0; channel < 4; channel++) {
                  int blockX = block % blockWrap, blockY = block / blockWrap;
                  int pixelX = pixel % pixelWrap, pixelY = pixel / pixelWrap;
                  var outIndex = ((blockY * 8 + pixelY) * width + (blockX * 8 + pixelX)) * 4 + channel;
                  imageOutput[outIndex] = array[j++];
               }
            }
         }

         var source = BitmapSource.Create(width, height, Dpi, Dpi, PixelFormats.Bgra32, null, imageOutput, 4 * width);
         return source;
      }
   }
}
