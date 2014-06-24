def sharedStruct(b)
   b.Short "ivSpread"
   b.ByteNum "level"
   b.Byte "unused"
   b.Species
end

def sharedAttacks(b)
   b.Short "attack1"; b.Short "attack2"
   b.Short "attack3"; b.Short "attack4"
end

trainerStruct0 = ->(b) {
   sharedStruct(b); b.Short "unused"
}
trainerStruct1 = ->(b) {
   sharedStruct(b); sharedAttacks(b); b.Short "unused"
}
trainerStruct2 = ->(b) {
   sharedStruct(b); b.Short "item"
}
trainerStruct3 = ->(b) {
   sharedStruct(b); sharedAttacks(b); b.Short "item"
}

trainers = types.FindVariableArray "wwwwwwwwwp", ->(b){
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
   b.Byte "unused"
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
