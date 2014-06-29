## -- Change every wild pokemon to be lvl 100
def updateLevels(ary, len)
   app.status "#{len}"
   return if ary==nil
   for i in 0..len-1
      ary[i].low = 100
      ary[i].high = 100
   end
end

for wild in self.wild
   updateLevels wild.grass.encounters, 12 if wild.grass != nil
   updateLevels wild.surf.encounters, 5   if wild.surf != nil
   updateLevels wild.tree.encounters, 5   if wild.tree != nil
   updateLevels wild.fish.encounters, 10  if wild.fish != nil
end

## -- Change each trainer's pokemon to be lvl 100
for trainer in self.trainer
   count = trainer.pokeCount
   for i in 0..count-1
      trainer.opponentPokemon[i].level = 100
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