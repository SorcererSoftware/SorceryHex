def evolve(pokemon)
   return if pokemon==nil
   return if self.evolutions[pokemon.species].index[0].evotype == 0
   pokemon.species = evolutions[pokemon.species].index[0].species
end

## -- Change every wild pokemon to be lvl 100
def updateLevels(ary, len)
   return if ary==nil
   for i in 0..len-1
      ary[i].low = 100
      ary[i].high = 100
      evolve ary[i]
      evolve ary[i]
   end
end

for w in self.wild
   updateLevels w.grass.encounters, 12 if w.grass != nil
   updateLevels w.surf.encounters, 5   if w.surf != nil
   updateLevels w.tree.encounters, 5   if w.tree != nil
   updateLevels w.fish.encounters, 10  if w.fish != nil
end

## -- Change each trainer's pokemon to be lvl 100
for t in self.trainer
   count = t.pokeCount
   for i in 0..count-1
      t.opponentPokemon[i].level = 100
      evolve t.opponentPokemon[i]
      evolve t.opponentPokemon[i]
   end
end

## -- evolve every pokemon in the game twice
#for instance in app.all("species")
#   next if evolutionData[instance.data][0].method == 0
#   instance.data = evolutionData[instance.data][0].species
#   app.writeBack instance
#   next if evolutionData[instance.data][0].method == 0
#   instance.data = evolutionData[instance.data][0].species
#   app.writeBack instance
#end