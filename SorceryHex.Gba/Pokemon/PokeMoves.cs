using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
   }
}
