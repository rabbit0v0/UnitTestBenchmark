# Scraping suitable classes for unit test evaluation

You are a researcher specialized in software testing.
Read the repo and find files that meets the following requirements.

*Important: Do not read the contents of the file unless there is a must*

*Important: Exercise the requirements in order, if one requirements is not fulfilled, the file is not qualified.*

## Basic Requirements

- The file should be a .cs file.
- The file should be less than 800 lines.

## Filter on Qualified Files

*Important: No actual tests should be generated duing the process.*

- The file should contain at least 2 methods.
- The code should be suitable for unit testing, consider cases use mocking, unit testing for the code should be challenging.
- Analyze what cases unit test should cover, the number of cases including edge case should be no less than 10.

## Report

- Summarize the qualified files
- Order the files in the following order:
    The complexity for LLM to generate the unit tests (consider mocking and different cases), descending