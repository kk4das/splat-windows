/* Port-assessment probes: confirm on THIS toolchain that the problems predicted in
   reference/port-assessment.md are real, before Stage 2 fixes them.
   usage: portprobes heap | textmode <dir> | colon <dir> | home */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>
#include <unistd.h>

int main(int argc, char **argv)
{
	if (argc < 2) return 2;

	if (strcmp(argv[1], "heap") == 0) {          /* P2: the fix (heap) must work for a 3.4 GB grid */
		size_t sz = (size_t)3400 * 1024 * 1024;
		unsigned char *p = (unsigned char *)calloc(sz, 1);
		if (!p) { printf("HEAP FAIL calloc %zu MB\n", sz >> 20); return 1; }
		unsigned long sum = 0;
		for (size_t i = 0; i < sz; i += 4096) { p[i] = 1; sum += p[i]; }
		free(p);
		printf("HEAP OK %zu MB allocated and touched (%lu pages)\n", sz >> 20, sum);
		return 0;
	}

	if (strcmp(argv[1], "textmode") == 0 && argc == 3) { /* P3: open(O_RDONLY) is text mode */
		char path[512];
		snprintf(path, sizeof path, "%s/bytes.bin", argv[2]);
		const unsigned char bytes[8] = {0x0D, 0x0A, 0x41, 0x1A, 0x42, 0x43, 0x0D, 0x0A};
		FILE *f = fopen(path, "wb"); fwrite(bytes, 1, 8, f); fclose(f);
		unsigned char buf[16];
		int fd = open(path, O_RDONLY); int t = (int)read(fd, buf, 16); close(fd);
		fd = open(path, O_RDONLY | O_BINARY); int b = (int)read(fd, buf, 16); close(fd);
		printf("TEXTMODE wrote=8 read(O_RDONLY)=%d read(O_RDONLY|O_BINARY)=%d\n", t, b);
		return 0;
	}

	if (strcmp(argv[1], "colon") == 0 && argc == 3) {  /* P1: colon in SDF filename */
		char path[512];
		snprintf(path, sizeof path, "%s/40:41:74:75.sdf", argv[2]);
		FILE *f = fopen(path, "wb");
		if (f) { fputs("1200\n", f); fclose(f); }
		printf("COLON fopen(\"40:41:74:75.sdf\",\"wb\") -> %s\n", f ? "succeeded" : "failed");
		return 0;
	}

	if (strcmp(argv[1], "home") == 0) {             /* P4: HOME is usually unset */
		const char *h = getenv("HOME"), *u = getenv("USERPROFILE");
		printf("HOME %s, USERPROFILE %s\n", h ? "set" : "UNSET", u ? "set" : "UNSET");
		return 0;
	}
	return 2;
}
