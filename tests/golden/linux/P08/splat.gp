set grid
set yrange [258.563 to 390.420]
set encoding iso_8859_1
set term png
set title "SPLAT! HD Terrain Profile Between KK4DAS and K4HTA (121.78° Azimuth)"
set xlabel "Distance Between KK4DAS and K4HTA (4.69 miles)"
set ylabel "Ground Elevation Above Sea Level (feet)"
set output "profile_hd.png"
plot "profile.gp" title "" with lines
