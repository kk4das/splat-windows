set grid
set yrange [56.311 to 123.960]
set encoding iso_8859_1
set term png
set title "SPLAT! Loss Profile Along Path Between KK4DAS and K4HTA (121.78° azimuth)"
set xlabel "Distance Between KK4DAS and K4HTA (7.55 kilometers)"
set ylabel "ITWOM Version 3.0 Path Loss (dB)"
set output "loss_metric.png"
plot "profile.gp" title "Path Loss" with lines
