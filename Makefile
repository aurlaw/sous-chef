.PHONY: migrate setup-native-libs

migrate: ## Run EF Core migrations manually
	dotnet ef database update \
		--project SousChef.Infrastructure \
		--startup-project SousChef.Api

setup-native-libs: ## Install Tesseract + create dylib symlinks for macOS arm64 (run once)
	brew install tesseract
	ln -sf /opt/homebrew/lib/libleptonica.6.dylib /opt/homebrew/lib/libleptonica-1.82.0.dylib



# aspire resource <resource-name> rebuild