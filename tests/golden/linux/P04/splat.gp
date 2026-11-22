set grid
set yrange [224.339 to 600.147]
set y2range [-65.120 to 310.687]
set xrange [-0.5 to 4.000]
set encoding iso_8859_1
set term png
set title "SPLAT! Path Profile Between Legion_Hall and K4HTA (69.41° azimuth)\nWith First Fresnel Zone"
set xlabel "Distance Between Legion_Hall and K4HTA (3.73 miles)"
set ylabel "Normalized Height Referenced To LOS Path Between\nLegion_Hall and K4HTA (feet)"
set output "normheight.png"
plot "profile.gp" title "Point-to-Point Profile" with lines, "clutter.gp" title "Ground Clutter (30.00 feet)" with lines, "reference.gp" title "Line of Sight Path" with lines, "curvature.gp" axes x1y2 title "Earth's Curvature Contour" with lines, "fresnel.gp" axes x1y1 title "First Fresnel Zone (146.685 MHz)" with lines, "fresnel_pt_6.gp" title "80% of First Fresnel Zone" with lines
