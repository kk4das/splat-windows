/* Toolchain probe: libbz2 round trip using the same stream API splat.cpp uses
   (BZ2_bzReadOpen / BZ2_bzRead on a FILE* opened "rb").
   usage: bz2test write <file> | bz2test read <file>
   The pattern deliberately contains 0x0D 0x0A 0x1A bytes (the Windows text-mode traps). */
#include <stdio.h>
#include <string.h>
#include <bzlib.h>

#define N (1 << 20)
static unsigned char want[N], got[N];

static void pattern(void)
{
	unsigned int s = 12345u;
	for (int i = 0; i < N; i++) {
		s = s * 1103515245u + 12345u;
		want[i] = (i % 97 == 0) ? (unsigned char)"\x0D\x0A\x1A"[i % 3] : (unsigned char)(s >> 16);
	}
}

int main(int argc, char **argv)
{
	int err = 0;
	if (argc != 3) { fprintf(stderr, "usage: bz2test write|read file\n"); return 2; }
	pattern();
	if (strcmp(argv[1], "write") == 0) {
		FILE *f = fopen(argv[2], "wb");
		if (!f) { printf("BZ2 FAIL open\n"); return 1; }
		BZFILE *b = BZ2_bzWriteOpen(&err, f, 9, 0, 0);
		BZ2_bzWrite(&err, b, want, N);
		BZ2_bzWriteClose(&err, b, 0, NULL, NULL);
		fclose(f);
		printf(err == BZ_OK ? "BZ2 WROTE %d bytes\n" : "BZ2 FAIL write err=%d\n", err == BZ_OK ? N : err);
		return err == BZ_OK ? 0 : 1;
	}
	FILE *f = fopen(argv[2], "rb");
	if (!f) { printf("BZ2 FAIL open\n"); return 1; }
	BZFILE *b = BZ2_bzReadOpen(&err, f, 0, 0, NULL, 0);
	int total = 0, n;
	while (err == BZ_OK && total < N) {
		n = BZ2_bzRead(&err, b, got + total, N - total);
		if (n > 0) total += n;
	}
	int rerr = err;
	BZ2_bzReadClose(&err, b);
	fclose(f);
	if ((rerr == BZ_OK || rerr == BZ_STREAM_END) && total == N && memcmp(want, got, N) == 0) {
		printf("BZ2 OK %d bytes round-tripped\n", total);
		return 0;
	}
	printf("BZ2 MISMATCH err=%d bytes=%d\n", rerr, total);
	return 1;
}
