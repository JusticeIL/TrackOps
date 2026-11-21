"""Creates .env from .env.example, with a fresh random value for every CHANGE_ME.

    python tools/make_env.py

No Python? Run it through Docker instead (see README, "Run it locally"). It uses only the standard
library, never overwrites an existing .env, and prints the names it filled in, never the values.

Every generated value is safe for its place: the SQL Server passwords meet SQL Server's policy
(upper and lower case, digits and a symbol), the keys are 64 characters, and no value contains
"$", quotes, ";" or spaces, which would break the compose file or the connection strings.
"""
import pathlib
import secrets
import string
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
EXAMPLE = ROOT / ".env.example"
TARGET = ROOT / ".env"
PLACEHOLDER = "CHANGE_ME"
LETTERS_AND_DIGITS = string.ascii_letters + string.digits
SYMBOLS = "-_.!"
SQL_PASSWORDS = {"MSSQL_SA_PASSWORD", "APP_DB_PASSWORD"}
KEY_LENGTH = 64
PASSWORD_LENGTH = 32


def random_text(length, alphabet):
	return "".join(secrets.choice(alphabet) for _ in range(length))


def sql_password():
	"""Random, but always with each kind of character SQL Server's policy asks for."""
	required = [secrets.choice(string.ascii_uppercase), secrets.choice(string.ascii_lowercase),
				secrets.choice(string.digits), secrets.choice(SYMBOLS)]
	rest = list(random_text(PASSWORD_LENGTH - len(required), LETTERS_AND_DIGITS + SYMBOLS))
	characters = required + rest
	secrets.SystemRandom().shuffle(characters)
	return "".join(characters)


def value_for(name, example_value):
	if name == "ACCEPT_EULA":
		# Y accepts Microsoft's licence for the SQL Server Developer image, as the README says.
		value = "Y"
	elif name in SQL_PASSWORDS:
		value = sql_password()
	elif example_value.startswith(PLACEHOLDER):
		value = random_text(KEY_LENGTH, LETTERS_AND_DIGITS)
	else:
		value = example_value
	return value


def main():
	if TARGET.exists():
		sys.exit(".env already exists; nothing was changed. Delete it first to make a new one.")

	lines, filled = [], []
	for line in EXAMPLE.read_text(encoding="utf-8").splitlines():
		name, separator, example_value = line.partition("=")
		if separator and not line.lstrip().startswith("#") and PLACEHOLDER in example_value:
			lines.append(f"{name}={value_for(name.strip(), example_value.strip())}")
			filled.append(name.strip())
		else:
			lines.append(line)

	TARGET.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
	print(f"Wrote {TARGET.name} with new values for: {', '.join(filled)}")
	print("ACCEPT_EULA=Y means you accept Microsoft's licence for the SQL Server Developer image.")


if __name__ == "__main__":
	main()
