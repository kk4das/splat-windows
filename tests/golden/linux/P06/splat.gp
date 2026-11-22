set grid
set yrange [250.033 to 587.270]
set encoding iso_8859_1
set term png
set title "SPLAT! Terrain Profile Between KK4DAS and K4HTA (121.78° Azimuth)"
set xlabel "Distance Between KK4DAS and K4HTA (4.69 miles)"
set ylabel "Ground Elevation Above Sea Level (feet)"
set output "profile_udt.png"
plot "profile.gp" title "" with lines
