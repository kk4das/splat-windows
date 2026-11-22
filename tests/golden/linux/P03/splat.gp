set grid
set yrange [-310.712 to 28.730]
set y2range [-601.686 to -262.244]
set xrange [-0.5 to 4.000]
set encoding iso_8859_1
set term png
set title "SPLAT! Path Profile Between Legion_Hall and K4HTA (69.41° azimuth)\nWith First Fresnel Zone"
set xlabel "Distance Between Legion_Hall and K4HTA (3.73 miles)"
set ylabel "Height Referenced To LOS Path Between\nLegion_Hall and K4HTA (feet)"
set output "height.png"
plot "profile.gp" title "Point-to-Point Profile" with lines, "reference.gp" title "Line of Sight Path" with lines, "curvature.gp" axes x1y2 title "Earth's Curvature Contour" with lines, "fresnel.gp" axes x1y1 title "First Fresnel Zone (146.685 MHz)" with lines, "fresnel_pt_6.gp" title "60% of First Fresnel Zone" with lines
