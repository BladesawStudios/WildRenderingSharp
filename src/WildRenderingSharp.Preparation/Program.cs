using WildRenderingSharp.Preparation;

// ensure-system --romfs <dir> [--cache <dir>]
// prepare --romfs <dir> --actor <name> [--game totk|botw] [--cache <dir>] [--mod <romfs dir>]... [--no-anims] [--force]
// prepare-batch --romfs <dir> --list <file> [--cache <dir>] [--jobs <n>] [--mod <romfs dir>]... [--no-anims] [--force] [--verbose]
// prepare-bake --romfs <dir> [--list <file>] [--cache <dir>] [--jobs <n>] [--force]
//
// Progress goes to stdout; exit code 0 on success, 1 on failure (error on stderr), 2 for a bad command line. `prepare` ends with
// "WRS_RESULT <model>". The batch commands print WRS_BEGIN <name>, WRS_DONE <name>\t<model> and WRS_FAIL <name>\t<message> lines instead of progress,
// so a batch killed partway (a native decoder can abort the process) leaves the names begun but not finished as the suspects.

return Cli.Run(args);
