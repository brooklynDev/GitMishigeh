SHELL := /bin/bash

APP_PROJECT := GitMishigeh/GitMishigeh.csproj
SOLUTION := GitMishigeh.sln
PACKAGE_SCRIPT := scripts/package-macos.sh
WINDOWS_PACKAGE_SCRIPT := scripts/package-windows.sh
LINUX_PACKAGE_SCRIPT := scripts/package-linux.sh
MAC_RUN_SCRIPT := scripts/run-macos.sh

.PHONY: build run publish

build:
	dotnet build $(SOLUTION) -p:UsedAvaloniaProducts=

run:
	@if [[ "$$(uname -s)" == "Darwin" ]]; then \
		$(MAC_RUN_SCRIPT); \
	else \
		dotnet run --project $(APP_PROJECT) -p:UsedAvaloniaProducts=; \
	fi

publish:
	$(PACKAGE_SCRIPT)
	$(WINDOWS_PACKAGE_SCRIPT)
	$(LINUX_PACKAGE_SCRIPT)
