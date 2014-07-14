using System.Collections.Generic;

namespace SorceryHex.Gba.Pokemon {
   class PokeMoves {
      // learnable moves : short.split(7,9) -> level,move (can search for this!)
      // tms : bitfield
      // hms : ??
      // tutors : bitfield
      // egg moves: == FFFF -> end of list
      //    >= 4E20 -> end of that pokemon, start next
      //    - 4E20 -> pokemon index

      // need custom interpretation for each of these, because they're too tight.

      public IDictionary<int, IList<int>> ReadEggMoves(byte[] data, int location) {
         const int key = 0x4E20;
         var eggMoves = new Dictionary<int, IList<int>>();
         int poke = 0;
         IList<int> moves = null;
         for (int value = data.ReadData(2, location); value != 0xFFFF; location += 2) {
            if (value < key) {
               moves.Add(value);
               continue;
            }
            value -= key;
            if (moves != null) eggMoves[poke] = moves;
            moves = new List<int>();
            poke = value;
         }
         return eggMoves;
      }

      public void ReadLearnableMove(byte[] data, int location, out byte level, out short move) {
         var combo = data.ReadShort(location);
         level = (byte)(combo >> 9);
         move = (short)(combo & 0x1FF);
      }
   }
}
