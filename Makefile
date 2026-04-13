VERSION ?= $(shell git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || echo "dev")
BINARY   = cbr2cbz.exe
DIST_DIR = dist
TARBALL  = cbr2cbz-$(VERSION)-linux.tar.gz

.PHONY: all build clean run dist

all: build

build: $(BINARY)

$(BINARY): CbrToCbzConverter.cs
	mcs -pkg:gtk-sharp-3.0 -out:$(BINARY) CbrToCbzConverter.cs

clean:
	rm -f $(BINARY)
	rm -rf $(DIST_DIR)
	rm -f cbr2cbz-*.tar.gz

run: build
	mono $(BINARY)

dist: build
	mkdir -p $(DIST_DIR)
	cp $(BINARY) $(DIST_DIR)/
	printf '#!/bin/sh\nmono "$$(dirname "$$0")/cbr2cbz.exe" "$$@"\n' > $(DIST_DIR)/cbr2cbz
	chmod +x $(DIST_DIR)/cbr2cbz
	cp README.md LICENSE $(DIST_DIR)/
	tar -czf $(TARBALL) -C $(DIST_DIR) .
	rm -rf $(DIST_DIR)
	@echo "Created: $(TARBALL)"
