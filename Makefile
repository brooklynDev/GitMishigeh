SHELL := /bin/bash

APP_PROJECT := GitMishigeh/GitMishigeh.csproj
SOLUTION := GitMishigeh.sln
PACKAGE_SCRIPT := scripts/package-macos.sh
WINDOWS_PACKAGE_SCRIPT := scripts/package-windows.sh
LINUX_PACKAGE_SCRIPT := scripts/package-linux.sh

.PHONY: build run publish

build:
	dotnet build $(SOLUTION) -p:UsedAvaloniaProducts=

run:
	dotnet run --project $(APP_PROJECT) -p:UsedAvaloniaProducts=

publish:
	$(PACKAGE_SCRIPT)
	$(WINDOWS_PACKAGE_SCRIPT)
	$(LINUX_PACKAGE_SCRIPT)
