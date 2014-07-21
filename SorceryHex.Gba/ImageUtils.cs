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
      public static void CalculateLZSizes(ISegment segment, out int uncompressed, out int compressed) {
         // all LZ compressed data starts with 0x10
         if (segment[0] != 0x10) {
            uncompressed = compressed = -1;
            return;
         }

         int length = segment.Read(1, 3);
         uncompressed = 0; compressed = 4;
         int offset = 4;

         while (true) {
            // always start with a bitfield
            // it encodes the next 8 steps
            // "1" means its a runlength dictionary compression
            //     and the dictionary is the most recent decompressed data
            // "0" means its decompressed
            // this makes a fully compressed data stream only 12.5% longer than it was when it started (at worst).
            var bitField = segment[offset++];
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

                  var byte1 = segment[offset++];
                  compressed++;
                  var runLength = (byte1 >> 4) + 3;
                  var runOffset_upper = (byte1 & 0xF) << 8;
                  var runOffset_lower = segment[offset++];
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

      public static ISegment UncompressLZ(ISegment segment) {
         Debug.Assert(segment.Length > 0);

         // all LZ compressed data starts with 0x10
         if (segment[0] != 0x10) return null;
         int length = segment.Read(1, 3);
         var uncompressed = new List<byte>();
         int offset = 4;

         while (true) {
            // always start with a bitfield
            // it encodes the next 8 steps
            // "1" means its a runlength dictionary compression
            //     and the dictionary is the most recent decompressed data
            // "0" means its decompressed
            // this makes a fully compressed data stream only 12.5% longer than it was when it started (at worst).
            var bitField = segment[offset++];
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

                  if (offset >= segment.Length) return null;
                  var byte1 = segment[offset++];
                  var runLength = (byte1 >> 4) + 3;
                  var runOffset_upper = (byte1 & 0xF) << 8;
                  var runOffset_lower = segment[offset++];
                  var runOffset = (runOffset_lower | runOffset_upper) + 1;
                  if (runOffset > uncompressed.Count) return null;
                  foreach (var i in Enumerable.Range(0, runLength)) {
                     uncompressed.Add(uncompressed[uncompressed.Count - runOffset]);
                     if (uncompressed.Count == length) return new GbaSegment(uncompressed.ToArray(), 0, length);
                  }
               } else {
                  uncompressed.Add(segment[offset++]);
                  if (uncompressed.Count == length) return new GbaSegment(uncompressed.ToArray(), 0, length);
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

      public static double ImageNoise(ISegment segment, int width, int height) {
         var image4bit = new byte[width * height];

         // convert 1 byte to 2 pixels
         int length = width * height / 2;
         for (int i = 0; i < length; i++) {
            var paletteIndex = (byte)(segment[i] & 0xF);
            var j = i * 2;
            image4bit[j] = paletteIndex;

            paletteIndex = (byte)(segment[i] >> 4);
            j++;
            image4bit[j] = paletteIndex;
         }

         // reorder into blocks
         int image4bitIndex = 0;
         int blockWrap = width / 8, pixelWrap = 8;
         var orderedImage = new byte[width, height];
         for (int block = 0; block < (width * height / 64); block++) {
            int blockX = block % blockWrap, blockY = block / blockWrap;
            for (int pixel = 0; pixel < 64; pixel++) {
               int pixelX = pixel % pixelWrap, pixelY = pixel / pixelWrap;
               orderedImage[blockX * 8 + pixelX, blockY * 8 + pixelY] = image4bit[image4bitIndex];
               image4bitIndex++;
            }
         }

         // count horizontal / vertical noise in the pixels
         int noise = 0;
         if (orderedImage[1, 0] != orderedImage[0, 0]) noise++;
         if (orderedImage[0, 1] != orderedImage[0, 0]) noise++;

         for (int x = 1; x < width; x++) for (int y = 1; y < height; y++) {
               if (orderedImage[x, y] != orderedImage[x - 1, y]) noise++;
               if (orderedImage[x, y] != orderedImage[x, y - 1]) noise++;
            }

         return (double)noise / (width * height);
      }

      public static BitmapSource Expand16bitImage(ISegment segment, Palette palette32bit, int width, int height) {
         // image16bit is organized as follows:
         // each byte contains 2 pixels, values 0-0xF
         // each set of 8x8 pixels is stored in a block
         // so the image data is 8x8 blocks of 8x8 pixels
         // this might be to helps with compression
         var image32bit = new byte[segment.Length * 8];
         for (int i = 0; i < segment.Length; i++) {
            int paletteIndex = segment[i] & 0xF;
            palette32bit.Write(image32bit, i * 2 + 0, paletteIndex);

            paletteIndex = segment[i] >> 4;
            palette32bit.Write(image32bit, i * 2 + 1, paletteIndex);
         }

         return Reorder(image32bit, width, height);
      }

      public class Palette {
         readonly byte[] colors;
         public readonly Color[] Colors = new Color[0x10];

         public Palette(ISegment palette) {
            Debug.Assert(palette.Length == 0x20);
            var length = palette.Length / 2;
            colors = new byte[length * 4];
            for (int i = 0; i < length; i++) {
               var full = (short)palette.Read(i * 2, 2);
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
            int blockX = block % blockWrap, blockY = block / blockWrap;
            for (int pixel = 0; pixel < 64; pixel++) {
               for (int channel = 0; channel < 4; channel++) {
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
