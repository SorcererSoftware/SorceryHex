def updateStats(poke, newtotal)
   # sum up the stats and make them add up to the new total
   oldtotal = poke.health + poke.attack + poke.defense + poke.speed + poke.spattack + poke.spdefense
   ratio = newtotal.to_f / oldtotal # float division
   poke.health    = [255, (poke.health    * ratio).round].min
   poke.attack    = [255, (poke.attack    * ratio).round].min
   poke.defense   = [255, (poke.defense   * ratio).round].min
   poke.speed     = [255, (poke.speed     * ratio).round].min
   poke.spattack  = [255, (poke.spattack  * ratio).round].min
   poke.spdefense = [255, (poke.spdefense * ratio).round].min
   # TODO remove EV bonus
   # normalize catch rate using newtotal
   poke.catchrate = 100 if newtotal == 350
   poke.catchrate =  80 if newtotal == 400
   poke.catchrate =  50 if newtotal == 450
   poke.catchrate =  20 if newtotal == 600
end

for i in 1..pokecount-1
   poke = stats[i]
   app.status("#{i}...") if i%10 == 0
   if poke.egggroup1==0x0F && poke.egggroup2==0x0F
      updateStats poke, 600
   elsif evolutions[i].index[0].evotype != 0
      updateStats poke, 350
   else
      haslowerform = false
      for j in 1..i
         next if evolutions[j].index[0].evotype == 0
         for k in 0..4
            next if evolutions[j].index[k].evotype == 0
            if evolutions[j].index[k].species == i
               haslowerform = true
               break
            end
         end
         break if haslowerform
      end
      
      if haslowerform
         updateStats poke, 450
      else
         updateStats poke, 400
      end
   end
end