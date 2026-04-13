VERSION  ?= $(shell git describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || echo "dev")
DOTNET   ?= dotnet
DIST_DIR  = dist
TARBALL   = cbr2cbz-$(VERSION)-linux.tar.gz
RID       ?= linux-x64

.PHONY: all build clean run dist

all: build

build:
	$(DOTNET) build -c Release

clean:
	$(DOTNET) clean
	rm -rf $(DIST_DIR)
	rm -f cbr2cbz-*.tar.gz

run:
	$(DOTNET) run

dist:
	mkdir -p $(DIST_DIR)
	$(DOTNET) publish -c Release -r $(RID) --self-contained true \
		-p:PublishSingleFile=true \
		-p:Version=$(VERSION) \
		-o $(DIST_DIR)
	tar -czf $(TARBALL) -C $(DIST_DIR) .
	rm -rf $(DIST_DIR)
	@echo "Created: $(TARBALL)"
