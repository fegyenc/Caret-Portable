from markitdown_caret_email import Redactor
from markitdown_caret_email._model import Address


def redact(text, **kwargs):
    return Redactor(**kwargs).redact(text)


def test_emails_get_stable_numbers():
    text = "Write to anna.nowak@client.fr or jan@acme.pl, then ANNA.NOWAK@client.fr again."
    assert redact(text) == "Write to [EMAIL-1] or [EMAIL-2], then [EMAIL-1] again."


def test_iban_only_with_valid_check_digits():
    assert redact("IBAN: PL61 1090 1014 0000 0712 1981 2874") == "IBAN: [IBAN-1]"
    assert redact("Ref GB82WEST12345698765433") == "Ref GB82WEST12345698765433"


def test_card_numbers_with_luhn():
    assert redact("Card 4111 1111 1111 1111.") == "Card [CARD-1]."
    assert redact("Order 4111 1111 1111 1112.") == "Order 4111 1111 1111 1112."


def test_national_ids():
    assert redact("PESEL 44051401359") == "PESEL [ID-1]"
    assert redact("Numer 44051401358") == "Numer 44051401358"  # wrong check digit
    assert redact("DNI 12345678Z, NIE X1234567L") == "DNI [ID-1], NIE [ID-2]"
    assert redact("DNI 12345678A") == "DNI 12345678A"
    assert redact("NIR 1 84 03 76 451 089 96") == "NIR [ID-1]"
    assert redact("NINO AB 12 34 56 C") == "NINO [ID-1]"
    assert redact("NINO QQ 12 34 56 C") == "NINO QQ 12 34 56 C"  # Q is never issued


def test_phone_numbers():
    assert redact("Call +48 601 234 567 or +33 (0)1 23 45 67 89") == "Call [PHONE-1] or [PHONE-2]"
    assert redact("Tél : 01 23 45 67 89") == "Tél : [PHONE-1]"
    assert redact("Móvil: 612 345 678") == "Móvil: [PHONE-1]"
    assert redact("Fijo 912 34 56 78") == "Fijo [PHONE-1]"
    assert redact("tel. kom. 601-234-567") == "tel. kom. [PHONE-1]"
    assert redact("UK office 020 7946 0958") == "UK office [PHONE-1]"


def test_things_that_are_not_phone_numbers():
    for text in (
        "Budget: 100 000 000 EUR",
        "Invoice 2025-03-04",
        "Meeting at 10:30 on 03.04.2025",
        "Version 1.2.3",
        "Order number 4411",
    ):
        assert redact(text) == text, text


def test_people_from_headers_in_all_their_forms():
    r = Redactor(people=[Address("Anna Nowak", "anna@client.fr"), Address("Kowalski, Jan", "jan@acme.pl")])
    text = "Anna Nowak, Nowak, Anna, NOWAK Anna and Anna agreed with Jan Kowalski (Jan) and Kowalski."
    assert r.redact(text) == (
        "[PERSON-1], [PERSON-1], [PERSON-1] and [PERSON-1] agreed with [PERSON-2] ([PERSON-2]) and [PERSON-2]."
    )


def test_single_names_only_when_capitalised():
    r = Redactor(people=[Address("Will Smith", "will@x.com")])
    assert r.redact("Will said it will work.") == "[PERSON-1] said it will work."


def test_name_wrapped_over_two_lines():
    r = Redactor(people=[Address("Anna Nowak", "")])
    assert r.redact("Signed by Anna\nNowak") == "Signed by [PERSON-1]"


def test_extra_names_from_the_user():
    r = Redactor(names=["ACME Polska", "Marta Wiśniewska"])
    assert r.redact("Marta Wiśniewska from ACME Polska called; Marta will write.") == (
        "[PERSON-1] from [PERSON-2] called; [PERSON-1] will write."
    )


def test_person_known_by_name_and_by_email_is_one_placeholder():
    r = Redactor(people=[Address("Anna Nowak", "anna@client.fr")], names=["Anna Nowak"])
    assert r.redact("Anna Nowak / Nowak") == "[PERSON-1] / [PERSON-1]"


def test_role_senders_are_masked_as_a_whole():
    r = Redactor(people=[Address("Microsoft Azure", "azure-noreply@microsoft.com")])
    assert r.redact("Microsoft Azure: deploy to Azure today.") == "[PERSON-1]: deploy to Azure today."


def test_placeholders_are_not_redacted_again():
    once = redact("anna@client.fr +48 601 234 567")
    assert redact(once) == once


def test_hyphenated_first_names_and_capitalised_surnames():
    # Directories write "Anna Maria NOWAK"; signatures and greetings write "Anna-Maria Nowak"
    r = Redactor(people=[Address("Anna Maria NOWAK", "anna@x.fr")])
    assert r.redact("Hi Anna-Maria, regards **Anna-Maria Nowak** / Nowak / NOWAK") == (
        "Hi [PERSON-1], regards **[PERSON-1]** / [PERSON-1] / [PERSON-1]"
    )
