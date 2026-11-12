#include <android/log.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

/* CLOCK_BOOTTIME matches SystemClock.elapsedRealtime. starttime in
   /proc/self/stat is ticks since boot and does not include suspend, so
   since-process matches the Java marks while the device stays awake. */

static long boot_ms(void)
{
	struct timespec ts;
	if (clock_gettime(CLOCK_BOOTTIME, &ts) != 0)
		return 0;
	return (long)ts.tv_sec * 1000L + (long)ts.tv_nsec / 1000000L;
}

static void faults(long *minflt, long *majflt, long *since, long now)
{
	char buf[1024];
	char *end;
	char *token;
	int field;
	int have_since;
	long ticks;
	long hz;
	FILE *file;

	*minflt = -1;
	*majflt = -1;
	*since = 0;
	have_since = 0;
	/* USAP pre-fork leaves /proc starttime earlier than
	   Process.getStartElapsedRealtime. The probe publishes that clock
	   before System.loadLibrary. */
	{
		const char *start_env = getenv("NATIVEPOC_START_ELAPSED");
		if (start_env != NULL && start_env[0] != '\0') {
			*since = now - atol(start_env);
			if (*since < 0)
				*since = 0;
			have_since = 1;
		}
	}
	file = fopen("/proc/self/stat", "r");
	if (file == NULL)
		return;
	if (fgets(buf, sizeof buf, file) == NULL) {
		fclose(file);
		return;
	}
	fclose(file);
	end = strrchr(buf, ')');
	if (end == NULL || end[1] == '\0')
		return;
	token = end + 2;
	field = 3;
	while (token != NULL && *token != '\0') {
		char *next = token;
		while (*next != '\0' && *next != ' ')
			next++;
		if (*next == ' ') {
			*next = '\0';
			next++;
		} else {
			next = NULL;
		}
		if (field == 10)
			*minflt = atol(token);
		else if (field == 12)
			*majflt = atol(token);
		else if (field == 22 && !have_since) {
			ticks = atol(token);
			hz = sysconf(_SC_CLK_TCK);
			if (hz <= 0)
				hz = 100;
			*since = now - ticks * 1000L / hz;
			if (*since < 0)
				*since = 0;
		}
		token = next;
		field++;
	}
}

static void mark(const char *phase)
{
	long now = boot_ms();
	long minflt;
	long majflt;
	long since;

	faults(&minflt, &majflt, &since, now);
	__android_log_print(
		ANDROID_LOG_INFO,
		"NativePoc",
		"startup clock=elapsed phase=%s t=%ld since-process-ms=%ld minflt=%ld majflt=%ld",
		phase,
		now,
		since,
		minflt,
		majflt);
}

/* Priority 101 runs after relocations, with the earliest constructors.
   65535 runs with the last ones. JNI_OnLoad is after every constructor. */
__attribute__((constructor(101))) static void so_reloc(void)
{
	mark("so-reloc");
}

__attribute__((constructor(65535))) static void so_ctors(void)
{
	mark("so-ctors");
}
