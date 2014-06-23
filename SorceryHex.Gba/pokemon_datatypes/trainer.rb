#opponentPokemon0: { -ivSpread -level -species -_ }
#opponentPokemon1: { -ivSpread -level -species -attack -attack -attack -attack -_ }
#opponentPokemon2: { -ivSpread -level -species -item }
#opponentPokemon3: { -ivSpread -level -species -item -attack -attack -attack -attack }

# FR: 23EAC8
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

   b.ByteNum "pokeCount"
   b.Byte "unused"
   b.Short "_"

   b.NullablePointer "opponentPokemon"
}
