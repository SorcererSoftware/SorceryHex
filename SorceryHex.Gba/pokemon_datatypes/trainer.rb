sharedStruct = ->(b) {
   b.Short "ivSpread"
   level = b.ByteNum "level"
   b.Assert (level<=100), "pokemon levels range from 1-100"
   b.Unused 1
   b.Species
}

sharedAttacks = ->(b) {
   b.ShortEnum "attack1", attackname
   b.ShortEnum "attack2", attackname
   b.ShortEnum "attack3", attackname
   b.ShortEnum "attack4", attackname
}

trainerStruct0 = ->(b) {
   sharedStruct.call(b); b.Unused 2
}
trainerStruct1 = ->(b) {
   sharedStruct.call(b); sharedAttacks.call(b); b.Unused 2
}
trainerStruct2 = ->(b) {
   sharedStruct.call(b); b.Short "item"
}
trainerStruct3 = ->(b) {
   sharedStruct.call(b); sharedAttacks.call(b); b.Short "item"
}

layout = types.FindVariableArray "wwwwwwwwwp", ->(b){
   pokeStructType = b.ByteNum "pokestructure"
   b.Assert (pokeStructType < 4), "pokeStruct must be 0-3"
   b.Byte "trainerClass"
   b.Byte "introMusic"
   b.Byte "sprite"

   b.String 12, "name"

   b.Word "gender_or_something"
   b.Word "moneyRate"
   b.Word "items"
   b.Word "unknown"

   count = b.ByteNum "pokeCount"
   b.Assert (count < 7), "trainers can't have more than 6 pokemon"
   b.Unused 1
   b.Short "_"

   if pokeStructType == 0
      b.Array "opponentPokemon", count, trainerStruct0
   elsif pokeStructType == 1
      b.Array "opponentPokemon", count, trainerStruct1
   elsif pokeStructType == 2
      b.Array "opponentPokemon", count, trainerStruct2
   else
      b.Array "opponentPokemon", count, trainerStruct3
   end
}

types.AddShortcut "trainer", layout.destination
self.trainer = layout.data