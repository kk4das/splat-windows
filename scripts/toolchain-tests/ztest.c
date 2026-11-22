/* Toolchain probe: zlib gz round trip (utils/fontdata.c uses gzopen/gzread).
   usage: ztest <file> */
#include <stdio.h>
#include <string.h>
#include <zlib.h>

int main(int argc, char **argv)
{
	static char want[65536], got[65536];
	if (argc != 2) return 2;
	for (int i = 0; i < (int)sizeof want; i++) want[i] = (char)((i * 7) ^ (i >> 5));
	gzFile w = gzopen(argv[1], "wb");
	if (!w || gzwrite(w, want, sizeof want) != (int)sizeof want) { printf("ZLIB FAIL write\n"); return 1; }
	gzclose(w);
	gzFile r = gzopen(argv[1], "rb");
	int n = r ? gzread(r, got, sizeof got) : -1;
	if (r) gzclose(r);
	if (n == (int)sizeof got && memcmp(want, got, sizeof got) == 0) {
		printf("ZLIB OK %d bytes round-tripped (zlib %s)\n", n, zlibVersion());
		return 0;
	}
	printf("ZLIB MISMATCH n=%d\n", n);
	return 1;
}
